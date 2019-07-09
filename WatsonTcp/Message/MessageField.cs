namespace WatsonTcp.Message
{
    using System;

    public class MessageField
    {
        #region Private-Fields

        private readonly int _BitNumber;
        private readonly string _Name;
        private readonly FieldType _Type;
        private readonly int _Length;

        #endregion

        #region Constructors

        public MessageField(int bitNumber, string name, FieldType fieldType, int length)
        {
            if (bitNumber < 0)
            {
                throw new ArgumentException("Invalid bit number.");
            }

            if (String.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (length < 0)
            {
                throw new ArgumentException("Invalid length.");
            }

            _BitNumber = bitNumber;
            _Name = name;
            _Type = fieldType;
            _Length = length;
        }

        #endregion

        #region Internal-Properties

        internal int BitNumber => _BitNumber;
        internal string Name => _Name;
        internal FieldType Type => _Type;
        internal int Length => _Length;

        #endregion
    }
}
