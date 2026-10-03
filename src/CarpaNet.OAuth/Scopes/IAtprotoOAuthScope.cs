namespace CarpaNet.OAuth.Scopes;

/// <summary>
/// A parsed atproto OAuth scope value (for example a <see cref="RepoPermission"/> or an <see cref="IncludeScope"/>).
/// </summary>
public interface IAtprotoOAuthScope
{
    /// <summary>
    /// Formats the scope as its normalized scope string.
    /// </summary>
    /// <returns>The scope string, e.g. <c>repo:app.bsky.feed.post?action=create</c>.</returns>
    string ToString();
}
