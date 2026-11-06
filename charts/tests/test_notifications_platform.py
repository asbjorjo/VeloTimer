import json
import unittest

from test_messagebus_topology import ROOT, render, rendered_topics, run
from test_migration_hooks import container


class NotificationsPlatformTests(unittest.TestCase):
    def named(self, documents, kind, app_name):
        return next(
            document for document in documents
            if document["kind"] == kind
            and (
                document["metadata"].get("labels", {}).get("app.kubernetes.io/name") == app_name
                or app_name in document["metadata"].get("name", "")
            )
        )

    def test_chart_and_provisioner_declare_non_session_refresh_subscription(self):
        topics = rendered_topics(render())
        topic = next(item for item in topics if item["name"] == "velotime-notifications")
        subscription = next(item for item in topic["subscriptions"] if item["name"] == "webui-notifications")
        self.assertFalse(subscription["requiresSession"])
        self.assertEqual(
            [{"name": "NotificationsRefreshRequestedV1",
              "sqlFilter": "MessageName = 'NotificationsRefreshRequestedV1'"}],
            subscription["rules"],
        )
        provisioner = json.loads(
            (ROOT / "src/MessageBus.Topology/topology.json").read_text(encoding="utf-8-sig")
        )["MessageBusTopology"]["Topics"]
        self.assertEqual(provisioner, topics)

    def test_api_receives_assertion_settings_and_external_secret_and_checks_redis_readiness(self):
        defaults = (ROOT / "charts/velotime/charts/module-notifications-api/values.yaml").read_text()
        self.assertNotIn("PRIVATE KEY", defaults)
        self.assertNotIn("PUBLIC KEY", defaults)
        documents = render(
            "module-notifications-api.existingSecret=notifications-api-trust",
            "module-notifications-api.sessionAssertion.issuer=VeloTime.WebUI.Mud",
            "module-notifications-api.sessionAssertion.audience=VeloTime.Module.Notifications.Api",
            "module-notifications-api.sessionAssertion.keyId=prod-key",
            "module-notifications-api.sessionAssertion.publicKeyPem=public-key-pem",
        )
        config = self.named(documents, "ConfigMap", "module-notifications-api")
        self.assertEqual("VeloTime.WebUI.Mud", config["data"]["Notifications__SessionAssertion__Issuer"])
        self.assertEqual("VeloTime.Module.Notifications.Api",
                         config["data"]["Notifications__SessionAssertion__Audience"])
        self.assertEqual("public-key-pem", config["data"]["Notifications__SessionAssertion__PublicKeys__prod-key"])
        deployment = self.named(documents, "Deployment", "module-notifications-api")
        pod_container = container(deployment)
        self.assertEqual("notifications-api-trust", pod_container["envFrom"][1]["secretRef"]["name"])
        self.assertEqual("/health", pod_container["readinessProbe"]["httpGet"]["path"])
        self.assertEqual("/alive", pod_container["livenessProbe"]["httpGet"]["path"])
        self.assertIn("ConnectionStrings__cache",
                      (ROOT / "charts/velotime/charts/module-notifications-api/templates/secrets.yaml").read_text())
        hosting = (ROOT / "src/Module.Notifications.Api/HostingExtensions.cs").read_text()
        self.assertIn('"VeloTime.Notifications"', hosting)
        self.assertIn("RedisXmlRepository", hosting)

    def test_api_rejects_partial_signing_key_trust_configuration(self):
        result = run("module-notifications-api.sessionAssertion.keyId=only-kid")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("keyId and publicKeyPem must be configured together", result.stderr)

    def test_processor_is_enabled_by_default_and_receives_runtime_dependencies(self):
        documents = render()
        processor = self.named(documents, "Deployment", "module-notifications-processor")
        self.assertEqual(
            "ghcr.io/asbjorjo/velotime-module-notifications-processor:2.1.0-alpha.33",
            container(processor)["image"],
        )
        self.assertIn(
            "ConnectionStrings__MessageBus",
            (ROOT / "charts/velotime/charts/module-notifications-processor/templates/secrets.yaml").read_text(),
        )
        self.assertIn("ConnectionStrings__velotimedb",
                      (ROOT / "charts/velotime/charts/module-notifications-processor/templates/secrets.yaml").read_text())
        self.assertIn("ConnectionStrings__cache",
                      (ROOT / "charts/velotime/charts/module-notifications-processor/templates/secrets.yaml").read_text())

    def test_webui_has_api_discovery_and_messagebus_secret(self):
        runtime_documents = render("webui.secrets.MessageBus=synthetic-bus")
        runtime_secret = self.named(runtime_documents, "Secret", "webui")
        self.assertEqual("synthetic-bus", runtime_secret["stringData"]["ConnectionStrings__MessageBus"])

        documents = render(
            "webui.existingSecret=webui-notification-signer",
            "webui.notifications.sessionAssertion.issuer=VeloTime.WebUI.Mud",
            "webui.notifications.sessionAssertion.audience=VeloTime.Module.Notifications.Api",
            "webui.notifications.sessionAssertion.keyId=prod-key",
        )
        config = self.named(documents, "ConfigMap", "webui")
        self.assertIn("services__module-notifications-api__http__0", config["data"])
        self.assertEqual("VeloTime.WebUI.Mud", config["data"]["Notifications__SessionAssertion__Issuer"])
        self.assertEqual("VeloTime.Module.Notifications.Api",
                         config["data"]["Notifications__SessionAssertion__Audience"])
        self.assertEqual("prod-key", config["data"]["Notifications__SessionAssertion__KeyId"])
        deployment = self.named(documents, "Deployment", "webui")
        self.assertEqual("webui-notification-signer",
                         container(deployment)["envFrom"][1]["secretRef"]["name"])
        container(self.named(documents, "Deployment", "webui"))

    def test_shared_valkey_has_persistent_storage_and_aof_for_protected_key_ring(self):
        documents = render()
        self.assertTrue(any(doc["kind"] == "PersistentVolumeClaim"
                            and doc["metadata"]["name"].startswith("cache-service")
                            for doc in documents))
        valkey = (ROOT / "charts/velotime/values.yaml").read_text()
        self.assertIn("appendonly yes", valkey)
        self.assertIn("appendfsync everysec", valkey)
        self.assertIn("keepPvc: true", valkey)

    def test_local_signing_configuration_is_ephemeral_and_api_migration_precedes_api(self):
        apphost = (ROOT / "src/AppHost/AppHost.cs").read_text()
        self.assertIn("RSA.Create(3072)", apphost)
        self.assertIn("ExportPkcs8PrivateKeyPem()", apphost)
        self.assertIn("ExportSubjectPublicKeyInfoPem()", apphost)
        self.assertNotIn("WriteAllText", apphost)
        self.assertIn("AddProject<Projects.VeloTime_Module_Notifications_Processor>", apphost)
        self.assertIn("notificationsapi.WaitForCompletion(notificationsmigrator)", apphost)
        self.assertIn("notificationsproc.WaitForCompletion(notificationsmigrator)", apphost)
        self.assertIn("notificationsproc.WaitForCompletion(messagebustopology)", apphost)
        self.assertIn("frontend.WaitForCompletion(messagebustopology)", apphost)

    def test_notifications_migration_job_is_enabled_by_api_migration_setting(self):
        documents = render()
        jobs = [
            doc for doc in documents
            if doc["kind"] == "Job"
            and doc["metadata"].get("labels", {}).get("velotime.io/migration-domain") == "notifications"
        ]
        self.assertEqual(1, len(jobs))
        self.assertIn("velotime-module-notifications-migration:", jobs[0]["spec"]["template"]["spec"]["containers"][0]["image"])

    def test_processor_migration_setting_also_enables_notifications_migration(self):
        documents = render(
            "module-notifications-api.migration.enabled=false",
            "module-notifications-processor.migration.enabled=true",
        )
        jobs = [
            doc for doc in documents
            if doc["kind"] == "Job"
            and doc["metadata"].get("labels", {}).get("velotime.io/migration-domain") == "notifications"
        ]
        self.assertEqual(1, len(jobs))


if __name__ == "__main__":
    unittest.main()
