using System;
using System.Collections.Generic;
using System.Text;

namespace WatsonTcp.Message
{
    public enum MessageStatus
    {
        Normal,
        Success,
        Failure,
        AuthRequired,
        AuthRequested,
        AuthSuccess,
        AuthFailure
    }
}
