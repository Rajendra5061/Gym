namespace GymManagement.Application.Common;

/// <summary>Base type for exceptions that map to a known HTTP status code.</summary>
public abstract class AppException : Exception
{
    public abstract int StatusCode { get; }
    public abstract string ErrorCode { get; }

    protected AppException(string message) : base(message) { }
    protected AppException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>400 – input failed validation.</summary>
public class ValidationAppException : AppException
{
    public override int StatusCode => 400;
    public override string ErrorCode => "VALIDATION_ERROR";
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public ValidationAppException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.") => Errors = errors;

    public ValidationAppException(string field, string message)
        : base("One or more validation errors occurred.") =>
        Errors = new Dictionary<string, string[]> { [field] = new[] { message } };
}

/// <summary>404 – the requested entity does not exist or is soft deleted.</summary>
public class NotFoundAppException : AppException
{
    public override int StatusCode => 404;
    public override string ErrorCode => "NOT_FOUND";

    public NotFoundAppException(string message) : base(message) { }

    public NotFoundAppException(string entityName, object key)
        : base($"{entityName} with identifier '{key}' was not found.") { }
}

/// <summary>409 – the operation conflicts with the current state or a unique constraint.</summary>
public class ConflictAppException : AppException
{
    public override int StatusCode => 409;
    public override string ErrorCode => "CONFLICT";
    public ConflictAppException(string message) : base(message) { }
}

/// <summary>422 – a domain rule forbids the operation.</summary>
public class BusinessRuleAppException : AppException
{
    public override int StatusCode => 422;
    public override string ErrorCode => "BUSINESS_RULE";
    public BusinessRuleAppException(string message) : base(message) { }
}

/// <summary>401 – the caller is not authenticated, or the credentials were rejected.</summary>
public class UnauthorizedAppException : AppException
{
    public override int StatusCode => 401;
    public override string ErrorCode => "UNAUTHORIZED";
    public UnauthorizedAppException(string message = "Authentication is required.") : base(message) { }
}

/// <summary>403 – authenticated but missing the required role or permission.</summary>
public class ForbiddenAppException : AppException
{
    public override int StatusCode => 403;
    public override string ErrorCode => "FORBIDDEN";
    public ForbiddenAppException(string message = "You do not have permission to perform this action.") : base(message) { }
}

/// <summary>402 – the trial has ended or the licence is invalid.</summary>
public class LicenseAppException : AppException
{
    public override int StatusCode => 402;
    public override string ErrorCode => "LICENSE_INVALID";
    public LicenseAppException(string message) : base(message) { }
}
