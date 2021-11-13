namespace VeloTimerWeb.Api.Util
{
    public class KeyVaultConfig : IKeyVaultConfig
    {
        public string KeyVaultName { get; set; }

        public string KeyVaultCertificateName { get; set; }

        public int KeyVaultRolloverHours { get; set; }
    }
}
