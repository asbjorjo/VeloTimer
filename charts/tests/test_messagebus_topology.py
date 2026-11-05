import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

import yaml


ROOT = Path(__file__).resolve().parents[2]
HELM = os.environ.get("HELM", "helm")
LABEL = "velotime.io/messagebus-topology"


def run(*settings, upgrade=False, release="topology-test", values=None):
    command = [HELM, "template", release, str(ROOT / "charts/velotime")]
    if upgrade:
        command.append("--is-upgrade")
    if values is not None:
        command.extend(["--values", values])
    for setting in settings:
        command.extend(["--set", setting])
    return subprocess.run(command, capture_output=True, text=True)


def render(*settings, **kwargs):
    result = run(*settings, **kwargs)
    if result.returncode != 0:
        raise AssertionError(result.stderr)
    return [document for document in yaml.safe_load_all(result.stdout) if document]


def topology(documents, kind):
    matches = [doc for doc in documents
               if doc["kind"] == kind and LABEL in doc["metadata"].get("labels", {})]
    return matches[0] if len(matches) == 1 else matches


def rendered_topics(documents):
    config = topology(documents, "ConfigMap")
    return json.loads(config["data"]["topology.json"])["MessageBusTopology"]["Topics"]


class MessageBusTopologyHookTests(unittest.TestCase):
    def test_default_install_and_upgrade_render_one_hook_job_before_migrations(self):
        for upgrade in (False, True):
            with self.subTest(upgrade=upgrade):
                documents = render(upgrade=upgrade)
                job = topology(documents, "Job")
                config = topology(documents, "ConfigMap")
                annotations = job["metadata"]["annotations"]
                self.assertEqual("pre-install,pre-upgrade", annotations["helm.sh/hook"])
                self.assertEqual("before-hook-creation,hook-succeeded", annotations["helm.sh/hook-delete-policy"])
                self.assertEqual("pre-install,pre-upgrade", config["metadata"]["annotations"]["helm.sh/hook"])
                self.assertLess(int(config["metadata"]["annotations"]["helm.sh/hook-weight"]),
                                int(annotations["helm.sh/hook-weight"]))
                migration_weights = [
                    int(doc["metadata"]["annotations"]["helm.sh/hook-weight"])
                    for doc in documents
                    if doc["kind"] == "Job" and "velotime.io/migration-domain" in doc["metadata"]["labels"]
                ]
                self.assertTrue(migration_weights)
                self.assertLess(int(annotations["helm.sh/hook-weight"]), min(migration_weights))
                self.assertEqual("Never", job["spec"]["template"]["spec"]["restartPolicy"])

    def test_job_uses_management_secret_and_mounted_topology(self):
        documents = render("messageBusTopology.existingSecret.name=sb-admin",
                           "messageBusTopology.existingSecret.key=connection")
        job = topology(documents, "Job")
        pod = job["spec"]["template"]["spec"]
        container = pod["containers"][0]
        env = {item["name"]: item for item in container["env"]}
        self.assertIn("velotime-messagebus-topology:", container["image"])
        self.assertEqual({"secretKeyRef": {"name": "sb-admin", "key": "connection"}},
                         env["ConnectionStrings__MessageBusManagement"]["valueFrom"])
        self.assertNotIn("ConnectionStrings__MessageBus", env)
        self.assertEqual("/config/topology.json", env["MessageBusTopologyFile"]["value"])
        self.assertEqual("/config", container["volumeMounts"][0]["mountPath"])
        self.assertEqual(topology(documents, "ConfigMap")["metadata"]["name"],
                         pod["volumes"][0]["configMap"]["name"])

    def test_chart_never_renders_the_management_secret(self):
        documents = render("messageBusTopology.existingSecret.name=sb-admin")
        self.assertFalse([doc for doc in documents
                          if doc["kind"] == "Secret" and doc["metadata"]["name"] == "sb-admin"])

    def test_missing_secret_name_fails_rendering(self):
        result = run("messageBusTopology.existingSecret.name=")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("messageBusTopology.existingSecret.name is required", result.stderr)

    def test_disabled_renders_nothing_and_skips_secret_requirement(self):
        documents = render("messageBusTopology.enabled=false", "messageBusTopology.existingSecret.name=")
        self.assertEqual([], topology(documents, "Job"))
        self.assertEqual([], topology(documents, "ConfigMap"))

    def test_chart_defaults_match_bundled_provisioner_topology(self):
        bundled = json.loads((ROOT / "src/MessageBus.Topology/topology.json").read_text(encoding="utf-8-sig"))
        self.assertEqual(bundled["MessageBusTopology"]["Topics"], rendered_topics(render()))

    def test_default_topology_has_no_subscription_without_filter_rules(self):
        for topic in rendered_topics(render()):
            for subscription in topic["subscriptions"]:
                with self.subTest(topic=topic["name"], subscription=subscription["name"]):
                    self.assertTrue(subscription["rules"])
                    for rule in subscription["rules"]:
                        self.assertNotEqual("1=1", rule.get("sqlFilter", "").replace(" ", ""))

    def test_topology_is_fully_replaceable_from_values(self):
        topics = [valid_topic()]
        topics[0]["subscriptions"][0]["rules"].append(
            {"name": "Correlation", "correlationFilter": {"subject": "x", "properties": {"MessageName": "B"}}})
        self.assertEqual(topics, rendered_topics(render_topics(topics)))

    def test_schema_and_template_reject_invalid_topology(self):
        def mutate(change):
            topic = valid_topic()
            change(topic)
            return [topic]

        subscription = lambda topic: topic["subscriptions"][0]
        rule = lambda topic: subscription(topic)["rules"][0]
        cases = {
            "subscription without rules": (mutate(lambda t: subscription(t).update(rules=[])), "minItems"),
            "subscription missing rules": (mutate(lambda t: subscription(t).pop("rules")), "rules"),
            "rule with both filters": (mutate(lambda t: rule(t).update(correlationFilter={"subject": "x"})), "oneOf"),
            "rule without filter": (mutate(lambda t: rule(t).pop("sqlFilter")), "oneOf"),
            "empty correlation filter": (mutate(lambda t: (rule(t).pop("sqlFilter"), rule(t).update(correlationFilter={}))), "minProperties"),
            "reserved rule name": (mutate(lambda t: rule(t).update(name="$Default")), "/name"),
            "unknown property": (mutate(lambda t: t.update(unknown="x")), "unknown"),
            "bad duration": (mutate(lambda t: subscription(t).update(lockDuration="1m")), "lockDuration"),
            "empty topic list": ([], "at least one topic"),
        }
        for case, (topics, reason) in cases.items():
            with self.subTest(case=case):
                result = run_topics(topics)
                self.assertNotEqual(0, result.returncode)
                self.assertIn(reason, result.stderr)


def valid_topic():
    return {
        "name": "custom",
        "maxSizeInMegabytes": 2048,
        "subscriptions": [{
            "name": "worker",
            "requiresSession": True,
            "maxDeliveryCount": 5,
            "lockDuration": "00:01:00",
            "rules": [{"name": "Sql", "sqlFilter": "MessageName = 'A'"}],
        }],
    }


def run_topics(topics):
    with tempfile.NamedTemporaryFile("w", suffix=".yaml", delete=False) as file:
        yaml.safe_dump({"messageBusTopology": {"topics": topics}}, file)
    try:
        return run(values=file.name)
    finally:
        os.unlink(file.name)


def render_topics(topics):
    result = run_topics(topics)
    if result.returncode != 0:
        raise AssertionError(result.stderr)
    return [document for document in yaml.safe_load_all(result.stdout) if document]

if __name__ == "__main__":
    unittest.main()
