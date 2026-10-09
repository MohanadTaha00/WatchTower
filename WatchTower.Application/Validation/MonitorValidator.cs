using System.Net;
using System.Text.RegularExpressions;
using WatchTower.Application.DTOs;
using WatchTower.Application.Exceptions;
using WatchTower.Application.Options;
using WatchTower.Domain.Entities;

namespace WatchTower.Application.Validation;

public static partial class MonitorValidator
{
    public const int MaxHeaders = 20;
    public const int MaxBodyLength = 10_000;
    public const int MaxKeywordLength = 200;

    private static readonly HashSet<string> AllowedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "HEAD", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"
    };

    private static readonly HashSet<string> ReservedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Content-Length", "Transfer-Encoding", "Connection"
    };

    [GeneratedRegex("^[!#$%&'*+.^_`|~0-9A-Za-z-]+$")]
    private static partial Regex HeaderNameRegex();

    /// <summary>
    /// Validates the request for its monitor type and returns a cleaned copy with
    /// trimmed values, defaults applied, and fields that don't apply to the type cleared.
    /// </summary>
    public static MonitorRequest Normalize(MonitorRequest request, MonitoringOptions options)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ValidationException("Name is required.");

        ValidateTiming(request.IntervalSeconds, request.TimeoutSeconds, options);

        var common = request with
        {
            Name = request.Name.Trim(),
            Url = null,
            ExpectedStatusCode = null,
            HttpMethod = null,
            RequestHeaders = null,
            RequestBody = null,
            ExpectedBodyContains = null,
            IpAddress = null,
            Port = null
        };

        return request.Type switch
        {
            MonitorType.Website => common with
            {
                Url = ValidateHttpUrl(request.Url, options),
                ExpectedStatusCode = ValidateStatusCode(request.ExpectedStatusCode)
            },
            MonitorType.Api => NormalizeApi(request, common, options),
            MonitorType.IpAddress => common with
            {
                IpAddress = ValidateIpAddress(request.IpAddress, options),
                Port = ValidatePort(request.Port)
            },
            _ => throw new ValidationException("Unknown monitor type.")
        };
    }

    private static MonitorRequest NormalizeApi(MonitorRequest request, MonitorRequest common, MonitoringOptions options)
    {
        var method = string.IsNullOrWhiteSpace(request.HttpMethod) ? "GET" : request.HttpMethod.Trim().ToUpperInvariant();
        if (!AllowedMethods.Contains(method))
            throw new ValidationException($"HTTP method must be one of: {string.Join(", ", AllowedMethods)}.");

        var body = string.IsNullOrEmpty(request.RequestBody) ? null : request.RequestBody;
        if (body is not null && method is "GET" or "HEAD")
            throw new ValidationException($"A request body can't be sent with {method}.");
        if (body is { Length: > MaxBodyLength })
            throw new ValidationException($"Request body must be at most {MaxBodyLength} characters.");

        var keyword = string.IsNullOrWhiteSpace(request.ExpectedBodyContains) ? null : request.ExpectedBodyContains.Trim();
        if (keyword is { Length: > MaxKeywordLength })
            throw new ValidationException($"Expected response text must be at most {MaxKeywordLength} characters.");
        if (keyword is not null && method == "HEAD")
            throw new ValidationException("HEAD responses have no body, so expected response text can't be checked.");

        return common with
        {
            Url = ValidateHttpUrl(request.Url, options),
            ExpectedStatusCode = ValidateStatusCode(request.ExpectedStatusCode),
            HttpMethod = method,
            RequestHeaders = ValidateHeaders(request.RequestHeaders),
            RequestBody = body,
            ExpectedBodyContains = keyword
        };
    }

    private static void ValidateTiming(int intervalSeconds, int timeoutSeconds, MonitoringOptions options)
    {
        if (intervalSeconds < options.MinIntervalSeconds || intervalSeconds > options.MaxIntervalSeconds)
            throw new ValidationException(
                $"Interval must be between {options.MinIntervalSeconds} and {options.MaxIntervalSeconds} seconds.");

        if (timeoutSeconds <= 0)
            throw new ValidationException("Timeout must be greater than zero.");

        if (timeoutSeconds >= intervalSeconds)
            throw new ValidationException("Timeout must be less than the monitoring interval.");
    }

    private static string ValidateHttpUrl(string? url, MonitoringOptions options)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ValidationException("URL is required.");

        url = url.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ValidationException("URL must be a valid absolute URI.");

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            throw new ValidationException("URL must use HTTP or HTTPS.");

        if (options.RequireHttpsUrls && uri.Scheme != Uri.UriSchemeHttps)
            throw new ValidationException("URL must use HTTPS.");

        UrlSafetyValidator.ValidateTargetUrl(uri, options);
        return url;
    }

    private static int ValidateStatusCode(int? expectedStatusCode)
    {
        var code = expectedStatusCode ?? 200;
        if (code is < 100 or > 599)
            throw new ValidationException("Expected status code must be between 100 and 599.");
        return code;
    }

    private static string ValidateIpAddress(string? ipAddress, MonitoringOptions options)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            throw new ValidationException("IP address is required.");

        if (!IPAddress.TryParse(ipAddress.Trim(), out var ip))
            throw new ValidationException("IP address must be a valid IPv4 or IPv6 address.");

        UrlSafetyValidator.ValidateTargetIp(ip, options);
        return ip.ToString();
    }

    private static int? ValidatePort(int? port)
    {
        if (port is null)
            return null;
        if (port is < 1 or > 65535)
            throw new ValidationException("Port must be between 1 and 65535.");
        return port;
    }

    private static Dictionary<string, string>? ValidateHeaders(Dictionary<string, string>? headers)
    {
        if (headers is null || headers.Count == 0)
            return null;

        if (headers.Count > MaxHeaders)
            throw new ValidationException($"At most {MaxHeaders} request headers are allowed.");

        var cleaned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rawName, rawValue) in headers)
        {
            var name = rawName?.Trim() ?? string.Empty;
            var value = rawValue ?? string.Empty;

            if (!HeaderNameRegex().IsMatch(name))
                throw new ValidationException($"'{name}' is not a valid header name.");
            if (ReservedHeaders.Contains(name))
                throw new ValidationException($"The {name} header is set automatically and can't be overridden.");
            if (value.Contains('\r') || value.Contains('\n'))
                throw new ValidationException($"The value of header '{name}' can't contain line breaks.");
            if (!cleaned.TryAdd(name, value.Trim()))
                throw new ValidationException($"Header '{name}' is listed more than once.");
        }

        return cleaned;
    }
}
