namespace WatchTower.Domain.Entities;

public enum CheckFailureReason
{
    Timeout,
    UnexpectedStatus,
    MissingKeyword,
    DnsFailure,
    ConnectionFailed,
    TlsFailure,
    NoPingReply,
    Other
}
