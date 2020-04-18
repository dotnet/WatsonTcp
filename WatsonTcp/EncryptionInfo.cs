using System;

namespace WatsonTcp
{
    public class EncryptionInfo
    {
        public EncryptionInfo(EncryptionType algorithm)
        {
            Algorithm = algorithm;
            Salt = RandomizeSalt();
        }
        
        /// <summary>
        /// The type of algorithm used in the message.
        /// </summary>
        public EncryptionType Algorithm = EncryptionType.None;
        
        /// <summary>
        /// The salt used in the encryption.
        /// </summary>
        public string Salt = null;

        private static string RandomizeSalt()
        {
            return Guid.NewGuid().ToString("N");
        }
    }
}