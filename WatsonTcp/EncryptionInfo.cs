using System;

namespace WatsonTcp
{
    public class EncryptionInfo
    {
        /// <summary>
        /// The type of algorithm used in the message.
        /// </summary>
        public EncryptionType Algorithm = EncryptionType.None;
        
        /// <summary>
        /// The salt used in the encryption.
        /// </summary>
        public string Salt = null;

        public void RandomizeSalt()
        {
            Salt = Guid.NewGuid().ToString("N");
        }
    }
}