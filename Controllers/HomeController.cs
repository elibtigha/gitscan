using System;
using System.Threading.Tasks;
using System.Web.Mvc;
using System.Web.Security;
using MS.Ess.Scan.DataContracts.Private;
using Octokit;
using OctokitDemo.Models;
using OctokitDemo.Scan;

namespace OctokitDemo.Controllers
{
    public class HomeController : Controller
    {
        // This URL uses the GitHub API to get a list of the current user's
        // repositories which include public and private repositories.
        public ActionResult Index()
        {
            if (Request.Url.Host == "127.0.0.1")
            {
                var builder = new UriBuilder(Request.Url)
                {
                    Host = "localhost"
                };
                string newUrl = builder.Uri.ToString();
                
                return Redirect(newUrl);
            }
            var model = new IndexViewModel();
            return View(model);
        }

        public ActionResult Scan(string repoId)
        {
            Guid requestId = Guid.NewGuid();
            try
            {
                GitHubClient client = GetGitHubClient();
                ScanRequestHandler scanner = new ScanRequestHandler();
                scanner.Start(client, Convert.ToInt64(repoId), requestId);
            }
            catch (AuthorizationException)
            {
                // Either the accessToken is null or it's invalid. This redirects
                // to the GitHub OAuth login page. That page will redirect back to the
                // Authorize action.
                return Redirect(GetOauthLoginUrl());
            }
            catch (Exception ex)
            {
            }

            return RedirectToAction("Complete", new { requestId = requestId });
        }

        public async Task<ActionResult> Report(string requestId)
        {
            ScanRequestHandler scanner = new ScanRequestHandler();
            MalwareDeterminationResult result = await scanner.GetScanResult(requestId);
            var reportModel = new ReportViewModel(result);
            return View(reportModel);
        }

        public async Task<ActionResult> Complete(string requestId)
        {
            ScanRequestHandler scanner = new ScanRequestHandler();
            bool result = await scanner.TryGetScanResult(requestId);

            if (!result)
            {
                //The processing  has not yet finished 
                //Add a refresh header, to refresh the page in 5 seconds.
                Response.Headers.Add("Refresh", "5");
                var model = new CompleteViewModel();
                return View(model);
            }

            return RedirectToAction("Report", new { requestId = requestId });
        }

        public async Task<ActionResult> List()
        {
            try
            {
                GitHubClient client = GetGitHubClient();
                // The following requests retrieves all of the user's repositories and
                // requires that the user be logged in to work.
                var repositories = await client.Repository.GetAllForCurrent();
                var model = new ListViewModel(repositories);

                return View(model);
            }
            catch (AuthorizationException)
            {
                // Either the accessToken is null or it's invalid. This redirects
                // to the GitHub OAuth login page. That page will redirect back to the
                // Authorize action.
                return Redirect(GetOauthLoginUrl());
            }
        }

        // This is the Callback URL that the GitHub OAuth Login page will redirect back to.h
        public async Task<ActionResult> Authorize(string code, string state)
        {
            if (!String.IsNullOrEmpty(code))
            {
                var expectedState = Session["CSRF:State"] as string;
                if (state != expectedState) throw new InvalidOperationException("SECURITY FAIL!");
                Session["CSRF:State"] = null;

                var token = await GetGitHubClient().Oauth.CreateAccessToken(
                    new OauthTokenRequest(
                        GitScanAppConfig.GetValue(Constants.GlobalSection, Constants.ClientIdKey),
                        GitScanAppConfig.GetValue(Constants.GlobalSection, Constants.ClientSecretKey),
                        code));
                Session["OAuthToken"] = token.AccessToken;
            }
            return RedirectToAction("List");
        }

        private string GetOauthLoginUrl()
        {
            string csrf = Membership.GeneratePassword(24, 1);
            Session["CSRF:State"] = csrf;
            // 1. Redirect users to request GitHub access
            var request = new OauthLoginRequest(GitScanAppConfig.GetValue(Constants.GlobalSection, Constants.ClientIdKey))
            {
                Scopes = { "repo" },
                State = csrf
            };
            var oauthLoginUrl = GetGitHubClient().Oauth.GetGitHubLoginUrl(request);
            return oauthLoginUrl.ToString();
        }

        private GitHubClient GetGitHubClient()
        {
            GitHubClient client =
            new GitHubClient(new ProductHeaderValue("GitScan"), new Uri("https://github.com/"));
            string accessToken = Session["OAuthToken"] as string;
            if (accessToken != null)
            {
                // This allows the client to make requests to the GitHub API on the user's behalf
                // without ever having the user's OAuth credentials.
                client.Credentials = new Credentials(accessToken);
            }

            return client;
        }
    }
}
