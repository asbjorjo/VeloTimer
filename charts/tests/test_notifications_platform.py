import json
import unittest

import yaml

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

    def test_api_uses_existing_keycloak_auth_and_runtime_secret_configuration(self):
        documents = render("module-notifications-api.existingSecret=notifications-api-runtime")
        config = self.named(documents, "ConfigMap", "module-notifications-api")
        self.assertNotIn("Notifications__SessionAssertion__Issuer", config["data"])
        self.assertNotIn("Notifications__SessionAssertion__Audience", config["data"])
        deployment = self.named(documents, "Deployment", "module-notifications-api")
        pod_container = container(deployment)
        self.assertEqual("notifications-api-runtime", pod_container["envFrom"][1]["secretRef"]["name"])
        self.assertEqual("/health", pod_container["readinessProbe"]["httpGet"]["path"])
        self.assertEqual("/alive", pod_container["livenessProbe"]["httpGet"]["path"])
        self.assertIn("ConnectionStrings__cache",
                      (ROOT / "charts/velotime/charts/module-notifications-api/templates/secrets.yaml").read_text())
        hosting = (ROOT / "src/Module.Notifications.Api/HostingExtensions.cs").read_text()
        self.assertIn('"VeloTime.Notifications"', hosting)
        self.assertIn("RedisXmlRepository", hosting)

    def test_legacy_session_assertion_values_do_not_render_or_configure_a_signer(self):
        api_values = (ROOT / "charts/velotime/charts/module-notifications-api/values.yaml").read_text()
        api_schema = (ROOT / "charts/velotime/charts/module-notifications-api/values.schema.json").read_text()
        webui_values = (ROOT / "charts/velotime/charts/webui/values.yaml").read_text()
        self.assertNotIn("sessionAssertion", api_values)
        self.assertNotIn("sessionAssertion", api_schema)
        self.assertNotIn("sessionAssertion", webui_values)

        documents = render(
            "module-notifications-api.sessionAssertion.issuer=legacy-issuer",
            "module-notifications-api.sessionAssertion.keyId=legacy-kid",
            "module-notifications-api.sessionAssertion.publicKeyPem=legacy-public-key",
            "webui.notifications.sessionAssertion.issuer=legacy-issuer",
            "webui.notifications.sessionAssertion.keyId=legacy-kid",
        )
        rendered = json.dumps(documents)
        self.assertNotIn("Notifications__SessionAssertion__", rendered)
        self.assertNotIn("legacy-public-key", rendered)
        self.assertNotIn("legacy-issuer", rendered)

    def test_api_is_internal_only_even_with_legacy_http_route_opt_in(self):
        defaults = render()
        self.assertFalse(any(
            document["kind"] == "HTTPRoute"
            and document["metadata"].get("name") == "module-notifications-api"
            for document in defaults
        ))
        webui_route = self.named(defaults, "HTTPRoute", "webui")
        self.assertEqual(
            {"type": "PathPrefix", "value": "/"},
            webui_route["spec"]["rules"][0]["matches"][0]["path"],
        )
        api_service = self.named(defaults, "Service", "module-notifications-api")
        self.assertEqual("ClusterIP", api_service["spec"]["type"])

        documents = render(
            "module-notifications-api.httpRoute.enabled=true",
            "module-notifications-api.httpRoute.hostnames[0]=same.example.net",
            "webui.httpRoute.hostnames[0]=same.example.net",
        )
        self.assertFalse(any(
            document["kind"] == "HTTPRoute"
            and document["metadata"].get("name") == "module-notifications-api"
            for document in documents
        ))
        self.assertEqual("ClusterIP",
                         self.named(documents, "Service", "module-notifications-api")["spec"]["type"])
        self.assertEqual(["same.example.net"],
                         self.named(documents, "HTTPRoute", "webui")["spec"]["hostnames"])
        self.assertEqual(
            {"type": "PathPrefix", "value": "/"},
            self.named(documents, "HTTPRoute", "webui")["spec"]["rules"][0]["matches"][0]["path"],
        )

    def test_ci_runs_notifications_api_and_worker_postgres_fixtures(self):
        workflow = yaml.safe_load((ROOT / ".github/workflows/ci.yml").read_text())
        job = workflow["jobs"]["build"]
        connection_names = (
            "NOTIFICATIONS_API_TEST_ADMIN_CONNECTION",
            "NOTIFICATIONS_WORKER_TEST_ADMIN_CONNECTION",
        )
        connections = [job["env"].get(name) for name in connection_names]
        self.assertTrue(all(connections))
        self.assertTrue(connections[0] == connections[1])
        self.assertTrue(all("Host=localhost;" in connection for connection in connections))
        self.assertEqual("postgres:17-alpine", job["services"]["postgres"]["image"])

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

        documents = render("webui.existingSecret=webui-runtime")
        config = self.named(documents, "ConfigMap", "webui")
        self.assertIn("services__module-notifications-api__http__0", config["data"])
        self.assertFalse(any(key.startswith("Notifications__SessionAssertion__")
                             for key in config["data"]))
        deployment = self.named(documents, "Deployment", "webui")
        self.assertEqual("webui-runtime",
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

    def test_apphost_has_no_notification_signing_infrastructure_and_preserves_dependencies(self):
        apphost = (ROOT / "src/AppHost/AppHost.cs").read_text()
        self.assertNotIn("SessionAssertion", apphost)
        self.assertNotIn("notification-session-assertion-private-key", apphost)
        self.assertNotIn("RSA.Create", apphost)
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
