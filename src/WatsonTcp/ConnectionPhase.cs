namespace WatsonTcp
{
    internal enum ConnectionPhase
    {
        Accepted = 0,
        TlsEstablished = 1,
        Authorizing = 2,
        PresharedKeyPending = 3,
        HandshakePending = 4,
        AwaitingRegistration = 5,
        Connected = 6,
        Rejected = 7,
        Disconnected = 8
    }
}
