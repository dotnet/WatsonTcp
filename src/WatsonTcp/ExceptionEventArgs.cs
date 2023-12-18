namespace WatsonTcp
{
    using System;

    /// <summary>
    /// Event arguments for when an exception is encountered. 
    /// </summary>
    public class ExceptionEventArgs
    {
        #region Public-Members

        /// <summary>
        /// Exception.
        /// </summary>
        public Exception Exception { get; }

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        internal ExceptionEventArgs(Exception e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));

            Exception = e;
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}
