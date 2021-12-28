using System;
using System.Text;

namespace WatsonTcp
{
    /// <summary>
    /// Encryption metadata for securing payload.
    /// </summary>
    public class EncryptionSettings
    {
        #region "Public-Members"
        /// <summary>
        /// Passphrase that must be consistent between clients and this server for encrypted communication.
        /// </summary>
        public string Passphrase { get; set; }

        /// <summary>
        /// Algorithm mechanism used to encrypt payload between clients and this server for encrypted communication.
        /// </summary>
        public EncryptionAlgorithm Algorithm { get; set; }
        #endregion

        #region "Constructors"
        public EncryptionSettings()
        {
            Algorithm = EncryptionAlgorithm.None;
            Passphrase = string.Empty;
        }

        public EncryptionSettings(EncryptionAlgorithm algorithm, string passphrase)
        {
            if (string.IsNullOrEmpty(passphrase) || Encoding.UTF8.GetBytes(passphrase).Length < 32)
            {
                throw new ArgumentOutOfRangeException(passphrase);
            }

            Algorithm = algorithm;
            Passphrase = passphrase;
        }
        #endregion
    }
}