namespace WatsonTcp.Message
{
    internal enum MessageStatus
    {
        Normal,
        Success,
        Failure,
        AuthRequired,
        AuthRequested,
        AuthSuccess,
        AuthFailure,
        Removed,
        Disconnecting
    }
}