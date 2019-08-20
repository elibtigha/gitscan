using GitHubJwt;

namespace ScanXGitHubApp
{
    public class JwtTokenHelper
    {
        public static GitHubJwtFactory CreateGitHubJwtFactory(IPrivateKeySource keySource, int appId)
        {
            // Use GitHubJwt library to create the GitHubApp Jwt Token using our private certificate PEM file
            GitHubJwtFactory generator = new GitHubJwtFactory(
                keySource,
                new GitHubJwtFactoryOptions
                {
                    AppIntegrationId = appId, // The GitHub App Id
                    ExpirationSeconds = 600 // 10 minutes is the maximum time allowed
                }
            );

            return generator;
        }
    }
}
