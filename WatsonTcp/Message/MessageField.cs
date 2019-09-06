using System;

namespace WatsonTcp.Message
{
    internal class MessageField
    {
        #region Public-Members

        internal int BitNumber { get; set; }
        internal string Name { get; set; }
        internal FieldType Type { get; set; }
        internal int Length { get; set; }

        #endregion Public-Members

        #region Constructors-and-Factories

        internal MessageField()
        { 
        }

        internal MessageField(int bitNumber, string name, FieldType fieldType, int length)
        {
            if (bitNumber < 0) throw new ArgumentException("Invalid bit number.");
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (length < 0) throw new ArgumentException("Invalid length.");

            BitNumber = bitNumber;
            Name = name;
            Type = fieldType;
            Length = length;
        }

        #endregion Constructors-and-Factories
    }
}