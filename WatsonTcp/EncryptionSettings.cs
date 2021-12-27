namespace WatsonTcp
{
    /// <summary>
    /// Encryption metadata for securing payload.
    /// </summary>
    public class EncryptionSettings
    {
        /// <summary>
        /// Passphrase that must be consistent between clients and this server for encrypted communication.
        /// </summary>
        public string Passphrase { get; set; }

        /// <summary>
        /// Algorithm mechanism used to encrypt payload between clients and this server for encrypted communication.
        /// </summary>
        public EncryptionAlgorithm Algorithm = EncryptionAlgorithm.None;
    }
}