﻿using System;

﻿namespace WatsonTcp
{
    internal class EncryptionHeader
    {
        public EncryptionHeader(EncryptionType algorithm)
        {
            Algorithm = algorithm;
        }
        
        /// <summary>
        /// The type of algorithm used to encrypt the data.
        /// </summary>
        public EncryptionType Algorithm = EncryptionType.None;
    }
}