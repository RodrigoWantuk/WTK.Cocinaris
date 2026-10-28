namespace Cocinaris.Api.Services;

/// <summary>
/// Normalizes post-authentication redirects to local application paths. It rejects absolute,
/// scheme-relative, backslash-prefixed, encoded-host, and malformed values so OIDC challenge and
/// sign-out endpoints cannot become open redirects.
/// </summary>
public static class ReturnUrlPolicy
{
    /// <summary>Gets a safe local redirect target, falling back to the application root.</summary>
    /// <param name="candidate">Untrusted return target supplied by a client.</param>
    /// <returns>A local absolute-path reference suitable for authentication properties.</returns>
    public static string Normalize(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || candidate[0] != '/' || !Uri.IsWellFormedUriString(candidate, UriKind.Relative))
        {
            return "/";
        }

        var decoded = candidate;
        for (var pass = 0; pass < 3; pass++)
        {
            try
            {
                decoded = Uri.UnescapeDataString(decoded);
            }
            catch (UriFormatException)
            {
                return "/";
            }

            if (!decoded.StartsWith("/", StringComparison.Ordinal) ||
                decoded.StartsWith("//", StringComparison.Ordinal) ||
                decoded.Contains("\\", StringComparison.Ordinal) ||
                decoded.Any(char.IsControl))
            {
                return "/";
            }
        }

        var pathOnly = decoded.Split('?', '#')[0];
        var normalizedPath = NormalizePath(pathOnly);
        if (IsBlockedPath(normalizedPath))
        {
            return "/";
        }

        var suffixStart = pathOnly.Length;
        return string.Concat(normalizedPath, decoded[suffixStart..]);
    }

    private static string NormalizePath(string path)
    {
        if (path is "/" or "")
        {
            return "/";
        }

        var stack = new List<string>();
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (stack.Count == 0)
                {
                    return "/";
                }

                stack.RemoveAt(stack.Count - 1);
                continue;
            }

            stack.Add(segment);
        }

        return stack.Count == 0 ? "/" : "/" + string.Join('/', stack);
    }

    private static bool IsBlockedPath(string path) =>
        path.Equals("/signin-oidc", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/signout-callback-oidc", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api/v1/auth/", StringComparison.OrdinalIgnoreCase);
}
