namespace VeloTimerWeb.Api.Util
{
    public interface IKeyVaultConfig
    {
        string KeyVaultName { get; }

        string KeyVaultCertificateName { get; }

        int KeyVaultRolloverHours { get; }
    }
}
