using System;

﻿namespace WatsonTcp
{
    internal class EncryptionInfo
    {
        public EncryptionInfo(EncryptionType algorithm)
        {
            Algorithm = algorithm;
        }
        
        /// <summary>
        /// The type of algorithm used to encrypt the data.
        /// </summary>
        public EncryptionType Algorithm = EncryptionType.None;
    }
}