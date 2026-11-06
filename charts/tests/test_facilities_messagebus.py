import subprocess
import unittest

from test_migration_hooks import container, render


class FacilitiesMessageBusTests(unittest.TestCase):
    def application(self, documents, kind):
        return next(
            document for document in documents
            if document["kind"] == kind
            and document["metadata"].get("labels", {}).get("app.kubernetes.io/name")
            == "module-facilities-api"
            and "velotime.io/migration-domain"
            not in document["metadata"].get("labels", {})
        )

    def test_configured_connection_string_reaches_application_secret(self):
        # Deliberately synthetic, with YAML-sensitive characters to check quoting.
        value = 'messagebus-test;Label="facilities: # test"'
        for upgrade in (False, True):
            with self.subTest(upgrade=upgrade):
                documents = render(
                    f"module-facilities-api.secrets.MessageBus={value}",
                    upgrade=upgrade,
                )
                secret = self.application(documents, "Secret")
                self.assertEqual(value, secret["stringData"]["ConnectionStrings__MessageBus"])
                self.assertEqual("Opaque", secret["type"])
                deployment = self.application(documents, "Deployment")
                self.assertEqual(
                    secret["metadata"]["name"],
                    container(deployment)["envFrom"][1]["secretRef"]["name"],
                )
                config_name = container(deployment)["envFrom"][0]["configMapRef"]["name"]
                config = next(document for document in documents
                              if document["kind"] == "ConfigMap"
                              and document["metadata"]["name"] == config_name)
                self.assertNotIn("ConnectionStrings__MessageBus", config["data"])

    def test_default_connection_string_is_empty_not_null(self):
        secret = self.application(render(), "Secret")
        self.assertEqual("", secret["stringData"]["ConnectionStrings__MessageBus"])

    def test_existing_secret_replaces_chart_managed_secret(self):
        for upgrade in (False, True):
            with self.subTest(upgrade=upgrade):
                documents = render(
                    "module-facilities-api.existingSecret=facilities-credentials",
                    "module-facilities-api.secrets.MessageBus=unused-test-value",
                    upgrade=upgrade,
                )
                deployment = self.application(documents, "Deployment")
                self.assertEqual(
                    "facilities-credentials",
                    container(deployment)["envFrom"][1]["secretRef"]["name"],
                )
                with self.assertRaises(StopIteration):
                    self.application(documents, "Secret")
                self.assertNotIn("unused-test-value", str(documents))

    def test_invalid_secret_configuration_fails_schema_validation(self):
        for setting in (
            "module-facilities-api.secrets.MessageBus=true",
            "module-facilities-api.secrets.MessageBus=123",
            "module-facilities-api.secrets.MessageBus[0]=invalid",
            "module-facilities-api.secrets=invalid",
            "module-facilities-api.existingSecret=true",
        ):
            with self.subTest(setting=setting):
                with self.assertRaises(subprocess.CalledProcessError) as error:
                    render(setting)
                self.assertIn("schema", error.exception.stderr.lower())


if __name__ == "__main__":
    unittest.main()
