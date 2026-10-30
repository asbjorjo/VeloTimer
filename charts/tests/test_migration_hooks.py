import os
from pathlib import Path
import subprocess
import unittest

import yaml


ROOT = Path(__file__).resolve().parents[2]
DOMAINS = ("facilities", "statistics", "timing", "profiles")
HELM = os.environ.get("HELM", "helm")


def render(*settings, upgrade=False, release="migration-test"):
    command = [HELM, "template", release, str(ROOT / "charts/velotime")]
    if upgrade:
        command.append("--is-upgrade")
    for setting in settings:
        command.extend(["--set", setting])
    result = subprocess.run(command, check=True, capture_output=True, text=True)
    return [document for document in yaml.safe_load_all(result.stdout) if document]


def migrations(documents, kind="Job"):
    return {
        document["metadata"]["labels"]["velotime.io/migration-domain"]: document
        for document in documents
        if document["kind"] == kind
        and "velotime.io/migration-domain" in document["metadata"].get("labels", {})
    }


def container(job):
    return job["spec"]["template"]["spec"]["containers"][0]


class MigrationHooksTests(unittest.TestCase):
    def test_default_install_and_upgrade_have_four_ordered_jobs_and_no_pod_migrations(self):
        for upgrade in (False, True):
            with self.subTest(upgrade=upgrade):
                documents = render(upgrade=upgrade)
                jobs = migrations(documents)
                self.assertEqual(set(DOMAINS), set(jobs))
                self.assertEqual(4, sum(doc["kind"] == "Job"
                                       and "velotime.io/migration-domain" in doc["metadata"].get("labels", {})
                                       for doc in documents))
                for domain, job in jobs.items():
                    annotations = job["metadata"]["annotations"]
                    self.assertEqual("pre-install,pre-upgrade", annotations["helm.sh/hook"])
                    self.assertEqual("before-hook-creation,hook-succeeded", annotations["helm.sh/hook-delete-policy"])
                    self.assertEqual(str(DOMAINS.index(domain)), annotations["helm.sh/hook-weight"])
                    self.assertEqual("Never", job["spec"]["template"]["spec"]["restartPolicy"])
                    self.assertIn(f"velotime-module-{domain}-migration:", container(job)["image"])
                    for kind in ("Secret", "ConfigMap"):
                        config = migrations(documents, kind)[domain]
                        self.assertLess(int(config["metadata"]["annotations"]["helm.sh/hook-weight"]),
                                        int(annotations["helm.sh/hook-weight"]))
                        self.assertEqual("before-hook-creation", config["metadata"]["annotations"]["helm.sh/hook-delete-policy"])
                for document in documents:
                    if document["kind"] == "Deployment":
                        for init in document["spec"]["template"]["spec"].get("initContainers") or []:
                            self.assertNotIn("-migration", init["image"])
                application_secrets = [doc for doc in documents if doc["kind"] == "Secret"
                                       and "velotime.io/migration-domain" not in doc["metadata"].get("labels", {})]
                self.assertGreaterEqual(len(application_secrets), 4)
                self.assertTrue(all("helm.sh/hook" not in doc["metadata"].get("annotations", {})
                                    for doc in application_secrets))

    def test_api_and_processor_toggles_are_combined(self):
        for domain in ("statistics", "timing"):
            for api, processor in ((False, False), (False, True), (True, False), (True, True)):
                with self.subTest(domain=domain, api=api, processor=processor):
                    documents = render(f"module-{domain}-api.migration.enabled={str(api).lower()}",
                                       f"module-{domain}-processor.migration.enabled={str(processor).lower()}")
                    self.assertEqual(api or processor, domain in migrations(documents))
                    self.assertEqual(api or processor, domain in migrations(documents, "Secret"))
                    self.assertEqual(api or processor, domain in migrations(documents, "ConfigMap"))

    def test_explicit_domain_enable_and_disable_override_consumers(self):
        for domain in DOMAINS:
            with self.subTest(domain=domain):
                if domain in ("facilities", "profiles"):
                    self.assertNotIn(domain, migrations(render(f"module-{domain}-api.migration.enabled=false")))
                self.assertNotIn(domain, migrations(render(f"migrationJobs.domains.{domain}.enabled=false")))
                settings = [f"module-{domain}-api.migration.enabled=false",
                            f"migrationJobs.domains.{domain}.enabled=true"]
                if domain in ("statistics", "timing"):
                    settings.append(f"module-{domain}-processor.migration.enabled=false")
                self.assertIn(domain, migrations(render(*settings)))

    def test_external_secrets_and_dedicated_migration_override(self):
        for domain in DOMAINS:
            with self.subTest(domain=domain):
                documents = render(f"module-{domain}-api.existingSecret=application-credentials")
                self.assertNotIn(domain, migrations(documents, "Secret"))
                self.assertEqual("application-credentials",
                                 container(migrations(documents)[domain])["envFrom"][1]["secretRef"]["name"])
                documents = render(f"module-{domain}-api.existingSecret=application-credentials",
                                   f"migrationJobs.domains.{domain}.existingSecret=migration-credentials")
                self.assertEqual("migration-credentials",
                                 container(migrations(documents)[domain])["envFrom"][1]["secretRef"]["name"])
                documents = render(f"module-{domain}-api.existingSecret=application-credentials",
                                   f"migrationJobs.domains.{domain}.existingSecret=")
                self.assertIn(domain, migrations(documents, "Secret"))

    def test_new_config_is_rendered_without_reading_old_release_objects(self):
        settings = ["module-statistics-api.database.host=api-db",
                    "module-statistics-processor.database.host=processor-db",
                    "module-statistics-api.migration.enabled=false",
                    "global.oidcAuthority=https://identity.example.test/",
                    "global.oidcAudience=https://api.example.test",
                    "module-statistics-api.fullnameOverride=custom-api"]
        documents = render(*settings, upgrade=True)
        secret = migrations(documents, "Secret")["statistics"]
        self.assertIn("Host=api-db;", secret["stringData"]["ConnectionStrings__velotimedb"])
        self.assertNotIn("processor-db", secret["stringData"]["ConnectionStrings__velotimedb"])
        documents = render(*settings, "migrationJobs.domains.statistics.database.host=updated-db",
                           "migrationJobs.domains.statistics.database.name=customdb", upgrade=True)
        secret = migrations(documents, "Secret")["statistics"]
        self.assertIn("Host=updated-db;", secret["stringData"]["ConnectionStrings__velotimedb"])
        self.assertIn("Database=customdb", secret["stringData"]["ConnectionStrings__velotimedb"])
        config = migrations(documents, "ConfigMap")["statistics"]["data"]
        self.assertEqual("https://identity.example.test/realms/velotime", config["ConnectionStrings__keycloak"])
        self.assertEqual("https://api.example.test", config["OIDC_AUDIENCE"])
        job = migrations(documents)["statistics"]
        self.assertEqual(secret["metadata"]["name"], container(job)["envFrom"][1]["secretRef"]["name"])

    def test_job_settings_and_image_overrides(self):
        documents = render("migrationJobs.backoffLimit=0", "migrationJobs.activeDeadlineSeconds=123",
                           "global.image.repository=registry.example.test/",
                           "global.image.tag=test-version",
                           "global.imagePullSecrets[0].name=registry-credentials",
                           "migrationJobs.domains.timing.image.pullPolicy=Never")
        for domain, job in migrations(documents).items():
            self.assertEqual(0, job["spec"]["backoffLimit"])
            self.assertEqual(123, job["spec"]["activeDeadlineSeconds"])
            self.assertEqual(f"registry.example.test/velotime-module-{domain}-migration:test-version",
                             container(job)["image"])
            self.assertEqual([{"name": "registry-credentials"}], job["spec"]["template"]["spec"]["imagePullSecrets"])
        self.assertEqual("Never", container(migrations(documents)["timing"])["imagePullPolicy"])

    def test_invalid_job_configuration_fails_rendering(self):
        for setting in ("migrationJobs.backoffLimit=-1", "migrationJobs.activeDeadlineSeconds=0",
                        "migrationJobs.domains.timing.enabled=not-a-boolean",
                        "migrationJobs.domains.timing.database.host="):
            with self.subTest(setting=setting):
                with self.assertRaises(subprocess.CalledProcessError):
                    render(setting)

    def test_long_release_names_remain_distinct_and_valid(self):
        names = []
        for suffix in ("one", "two"):
            jobs = migrations(render(release="a" * 45 + suffix))
            names.append({job["metadata"]["name"] for job in jobs.values()})
            self.assertTrue(all(len(name) <= 63 for name in names[-1]))
        self.assertFalse(names[0] & names[1])


if __name__ == "__main__":
    unittest.main()
