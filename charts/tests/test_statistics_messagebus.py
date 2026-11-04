import subprocess
import unittest

from test_migration_hooks import ROOT, container, render


class StatisticsMessageBusTests(unittest.TestCase):
    def application(self, documents, kind):
        return next(
            document for document in documents
            if document["kind"] == kind
            and document["metadata"].get("labels", {}).get("app.kubernetes.io/name")
            == "module-statistics-api"
            and "velotime.io/migration-domain"
            not in document["metadata"].get("labels", {})
        )

    def test_connection_reaches_secret_not_configmap_for_install_and_upgrade(self):
        value = 'synthetic-bus;Label="statistics: # test"'
        for upgrade in (False, True):
            with self.subTest(upgrade=upgrade):
                documents = render(f"module-statistics-api.secrets.MessageBus={value}", upgrade=upgrade)
                secret = self.application(documents, "Secret")
                self.assertEqual(value, secret["stringData"]["ConnectionStrings__MessageBus"])
                self.assertEqual("Opaque", secret["type"])
                deployment = self.application(documents, "Deployment")
                self.assertEqual(secret["metadata"]["name"],
                                 container(deployment)["envFrom"][1]["secretRef"]["name"])
                config_name = container(deployment)["envFrom"][0]["configMapRef"]["name"]
                config = next(doc for doc in documents
                              if doc["kind"] == "ConfigMap" and doc["metadata"]["name"] == config_name)
                self.assertNotIn("ConnectionStrings__MessageBus", config["data"])

    def test_default_is_empty_and_existing_secret_suppresses_managed_credentials(self):
        self.assertEqual("", self.application(render(), "Secret")["stringData"]["ConnectionStrings__MessageBus"])
        for upgrade in (False, True):
            with self.subTest(upgrade=upgrade):
                documents = render("module-statistics-api.existingSecret=statistics-credentials",
                                   "module-statistics-api.secrets.MessageBus=unused-test-value", upgrade=upgrade)
                deployment = self.application(documents, "Deployment")
                self.assertEqual("statistics-credentials",
                                 container(deployment)["envFrom"][1]["secretRef"]["name"])
                with self.assertRaises(StopIteration):
                    self.application(documents, "Secret")
                self.assertNotIn("unused-test-value", str(documents))

    def test_invalid_secret_shapes_fail_schema_validation(self):
        for setting in (
            "module-statistics-api.secrets.MessageBus=true",
            "module-statistics-api.secrets.MessageBus=123",
            "module-statistics-api.secrets.MessageBus[0]=invalid",
            "module-statistics-api.secrets=invalid",
            "module-statistics-api.existingSecret=true",
        ):
            with self.subTest(setting=setting):
                with self.assertRaises(subprocess.CalledProcessError) as error:
                    render(setting)
                self.assertIn("schema", error.exception.stderr.lower())

    def test_aspire_preserves_statistics_dependencies_and_adds_messagebus(self):
        source = (ROOT / "src/AppHost/AppHost.cs").read_text()
        registration = source.split("var statisticsapi =", 1)[1].split("var timingapi =", 1)[0]
        for reference in ("servicebusconnection", "cache", "velotimedb", "keycloak"):
            self.assertIn(f".WithReference({reference})", registration)
        self.assertIn(".WaitFor(velotimedb)", registration)


if __name__ == "__main__":
    unittest.main()
