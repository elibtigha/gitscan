using GitHubJwt;
using MS.Ess.Scan.DataContracts.Private;
using Octokit;
using OctokitDemo;
using OctokitDemo.Scan;
using OctokitDemo.Scan.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ScanXGitHubApp
{
    public class CheckSuiteRequestHandler
    {
        private GitHubJwtFactory jwtHelper;

        private readonly long GithubInstallationId;
        private readonly Repository CurrentRepository;
        private readonly string CommitSha;

        private readonly bool IsPullRequest;

        private IGitHubClient gitHubAppClient = null;
        private IGitHubClient gitHubInstallationClient = null;

        private DateTime appTokenExpirationTime = DateTime.MaxValue;
        private DateTime installationTokenExpirationTime = DateTime.MaxValue;
        private const int maxAppTokenValidityTimeInSeconds = 540; //9 min

        private readonly Guid RequestId;

        public CheckSuiteRequestHandler(CheckSuiteEventPayload payload, IPrivateKeySource keySource, Guid requestId)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            if (keySource == null)
            {
                throw new ArgumentNullException(nameof(keySource));
            }

            GithubInstallationId = payload.Installation.Id;
            CurrentRepository = payload.Repository;
            CommitSha = payload.CheckSuite.HeadSha;
            IsPullRequest = false;
            RequestId = requestId;
            Init(keySource);
        }

        public Guid CheckId { get; private set;  }

        public CheckSuiteRequestHandler(CheckRunEventPayload payload, IPrivateKeySource keySource, Guid requestId)
        {
            GithubInstallationId = payload.Installation.Id;
            CurrentRepository = payload.Repository;
            CommitSha = payload.CheckRun.HeadSha;
            IsPullRequest = false;
            RequestId = requestId;
            Init(keySource);
        }

        public CheckSuiteRequestHandler(PullRequestEventPayload payload, IPrivateKeySource keySource, Guid requestId)
        {
            GithubInstallationId = payload.Installation.Id;
            CurrentRepository = payload.Repository;
            CommitSha = payload.PullRequest.Head.Sha;
            IsPullRequest = true;
            RequestId = requestId;
            Init(keySource);
        }

        private void Init(IPrivateKeySource keySource)
        {
            CheckId = Guid.NewGuid();

            this.jwtHelper = JwtTokenHelper.CreateGitHubJwtFactory(
                   keySource, Constants.GitHubAppId);

            gitHubInstallationClient = this.CreateGitHubInstallationClientAsync().Result;
        }

        private string CreateAppToken()
        {
            string jwtToken = this.jwtHelper.CreateEncodedJwtToken();
            return jwtToken;
        }

        private IGitHubClient CreateGitHubAppClient()
        {
            this.appTokenExpirationTime = DateTime.UtcNow.AddSeconds(CheckSuiteRequestHandler.maxAppTokenValidityTimeInSeconds);
            string jwtToken = this.CreateAppToken();
            Credentials credentials = new Credentials(jwtToken, AuthenticationType.Bearer);
            IGitHubClient client = new GitHubClient(new ProductHeaderValue(Constants.AppName))
            {
                Credentials = credentials
            };
            return client;
        }

        private async Task<IGitHubClient> CreateGitHubInstallationClientAsync()
        {
            AccessToken accessToken = await this.CreateIntallationToken().ConfigureAwait(false);
            this.installationTokenExpirationTime = accessToken.ExpiresAt.UtcDateTime;

            // Create a new GitHubClient using the installation token as authentication
            var installationClient = new GitHubClient(new ProductHeaderValue($"{Constants.AppName}{GithubInstallationId}"))
            {
                Credentials = new Credentials(accessToken.Token)
            };

            return installationClient;
        }

        private bool IsGitHubAppClientValid()
        {
            return this.gitHubAppClient != null && DateTime.UtcNow < appTokenExpirationTime;
        }

        private bool IsGitHubInstallationClientValid()
        {
            return this.gitHubInstallationClient != null && DateTime.UtcNow < installationTokenExpirationTime;
        }

        private async Task<AccessToken> CreateIntallationToken()
        {
            if (!this.IsGitHubAppClientValid())
            {
                this.gitHubAppClient = this.CreateGitHubAppClient();
            }

            var response = await this.gitHubAppClient.GitHubApps.CreateInstallationToken(GithubInstallationId).ConfigureAwait(false);

            return response;
        }

        public Task Go()
        {
            return this.StartExecution();
        }

        private async Task StartExecution()
        {
            IReadOnlyList<Installation> installations = await this.gitHubAppClient.GitHubApps.GetAllInstallationsForCurrent().ConfigureAwait(false);

            try
            {
                if (!this.IsGitHubInstallationClientValid())
                {
                    throw new InvalidOperationException("Error: gitHubInstallationClient is invalid.");
                }

                if (IsPullRequest)
                {
                    ICheckSuitesClient checkSuiteClient = gitHubInstallationClient.Check.Suite;

                    CheckSuitesResponse x = await checkSuiteClient.GetAllForReference(CurrentRepository.Id, CommitSha).ConfigureAwait(false);
                    if (x.TotalCount > 0)
                    {
                        long checkSuiteId = x.CheckSuites.FirstOrDefault().Id;
                        bool res = await checkSuiteClient.Rerequest(CurrentRepository.Id, checkSuiteId);
                    }
                    else
                    {
                        var newCheckSuite = new NewCheckSuite(CommitSha);
                        try
                        {
                            CheckSuite suite = 
                                await checkSuiteClient.Create(
                                    CurrentRepository.Owner.Login, 
                                    CurrentRepository.Name, newCheckSuite)
                                .ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                        }
                    }

                    return;
                }

                ICheckRunsClient checkRunClient = gitHubInstallationClient.Check.Run;

                // Create a new heckRun in GitHub
                var newCheckRun = new NewCheckRun("ScanX", CommitSha)
                {
                    Status = CheckStatus.Queued,
                };

                CheckRun checkRun = 
                    await checkRunClient.Create(
                        CurrentRepository.Owner.Login, 
                        CurrentRepository.Name, 
                        newCheckRun)
                    .ConfigureAwait(false);
                
                // --- Downoad a ZIP ---
                byte[] buffer = await ScanHelper.DownloadRepoZip(gitHubInstallationClient, CurrentRepository.Id, CommitSha).ConfigureAwait(false);
                int size = buffer.Length;

                // Upload ZIP to a storage blob
                string blobName = $"{RequestId.ToString()}";
                string blobUri = await ScanHelper.UploadBufferToStorage(buffer, blobName);

                // Update check's status to "in progress"
                CheckRunUpdate checkRunUpdate = new CheckRunUpdate
                {
                    Status = CheckStatus.InProgress,
                    Name = checkRun.Name
                };
                checkRun = await checkRunClient.Update(CurrentRepository.Id, checkRun.Id, checkRunUpdate).ConfigureAwait(false);

                // --- Start a scan ---
                // Simulate sending of a message to a SB queue
                // Create worker notification message 
                MalwareDeterminationRequest scanRequest = new MalwareDeterminationRequest();
                scanRequest.ClientId = "GitHubScanX";
                scanRequest.FileName = $"{RequestId.ToString()}.zip";
                scanRequest.FileSizeInBytes = 1000; //dummy
                scanRequest.RequestId = RequestId;
                scanRequest.Uri = new Uri(blobUri);

                // Notify worker (aka put the notification message to a queue)
                ScanXMock mock = new ScanXMock();
                await mock.SendScanRequest(scanRequest).ConfigureAwait(false);

                // --- Poll for a scan completion ---
                MalwareDeterminationResult scanResult;
                do
                {
                    await Task.Delay(500).ConfigureAwait(false);
                    if (await mock.TryGetResult(RequestId))
                    {
                        scanResult = await mock.GetResult(RequestId).ConfigureAwait(false);
                        break;
                    }
                }
                while (true); //!!!! for POC only

                checkRunUpdate.Status = CheckStatus.Completed;
                checkRunUpdate.CompletedAt = DateTime.UtcNow;
                checkRunUpdate.Conclusion = scanResult.WorkStatus == WorkStatus.Clean ? CheckConclusion.Success : CheckConclusion.Failure;

                if (checkRunUpdate.Conclusion == CheckConclusion.Failure)
                {
                    checkRunUpdate.Output = new NewCheckRunOutput(
                        "Scan Report",
                        $"GitScan detected {scanResult.ConfirmedMalwares.Count()} infected files. See details below.");

                    checkRunUpdate.Output.Text = "| File Path| Malware Type| AV Engines|\n";
                    checkRunUpdate.Output.Text += "|:---|:---|:---|\n";

                    foreach(var entry in scanResult.ConfirmedMalwares)
                    {
                        checkRunUpdate.Output.Text += $"|{entry.FileName}|{entry.MalwareInfo}|{string.Join(",", entry.AvEngines.ToArray())}";
                    }
                }

                checkRun = await checkRunClient.Update(CurrentRepository.Id, checkRun.Id, checkRunUpdate).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception: {ex.Message}");
            }
        }
    }
}
