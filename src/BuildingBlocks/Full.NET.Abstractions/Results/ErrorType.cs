namespace Full.NET.Abstractions.Results;

public enum ErrorType
{
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    BusinessRule,
    RateLimited,
    Unexpected
}
