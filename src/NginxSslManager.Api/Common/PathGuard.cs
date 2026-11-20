namespace NginxSslManager.Api.Common;

/// <summary>
/// Small helpers that keep user supplied paths predictable.
/// </summary>
public static class PathGuard
{
    public static string RequireAbsolutePath(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ApiException($"{fieldName} 不能为空");
        }

        if (!Path.IsPathRooted(value))
        {
            throw new ApiException($"{fieldName} 必须是绝对路径");
        }

        if (value.Contains('\0'))
        {
            throw new ApiException($"{fieldName} 包含非法字符");
        }

        var full = Path.GetFullPath(value);
        if (full.Contains("/../", StringComparison.Ordinal))
        {
            throw new ApiException($"{fieldName} 不能包含 .. 路径");
        }

        return full;
    }

    public static void EnsureDistinct(string first, string second, string message)
    {
        if (string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.Ordinal))
        {
            throw new ApiException(message);
        }
    }
}
