using System;
using System.IO;
using System.Net.Http;
using System.Web;
using System.Web.Mvc;
using GitHubJwt;
using Octokit;
using OctokitDemo.Scan;
using ScanXGitHubApp;

namespace OctokitDemo.Controllers
{
    public class GithubEventsController : Controller
    {
        private static readonly Lazy<IPrivateKeySource> PrivateKeySource = new Lazy<IPrivateKeySource>(
            () =>
            {
                return new FilePrivateKeySource(Constants.GitHubAppPemFilePath);
            });
        // GET: GithubEvents
        [HttpPost]
        public ActionResult Default()
        {
            string actionName = Request.Headers.Get("X-GITHUB-EVENT");

            // Only the below events will be handled
            if (!actionName.Equals("check_suite", StringComparison.InvariantCultureIgnoreCase) &&
                !actionName.Equals("check_run", StringComparison.InvariantCultureIgnoreCase) &&
                !actionName.Equals("pull_request", StringComparison.InvariantCultureIgnoreCase))
            {
               return new HttpStatusCodeResult(200);
            }

            // Obtain the body signature
            string messageSignature = Request.Headers.Get("X-HUB-Signature");
            if (string.IsNullOrEmpty(messageSignature))
            {
               return new HttpStatusCodeResult(400);
            }

            // Read the body
            string body = GetRequestPostData(Request);

            // Validate message integrity
            if (!RequestPayloadHelper.ValidateSender(body, messageSignature, Constants.GitHubAppWebhookSecret))
            {
                return new HttpStatusCodeResult(400);
            }

            Guid requestId = Guid.NewGuid();

            if (actionName.Equals("check_run", StringComparison.InvariantCultureIgnoreCase))
            {
                CheckRunEventPayload checkRunPayload = RequestPayloadHelper.Parse<CheckRunEventPayload>(body);
 
                if (checkRunPayload.Action.Equals("rerequested", StringComparison.InvariantCultureIgnoreCase))
                {
                    CheckSuiteRequestHandler handler = new CheckSuiteRequestHandler(checkRunPayload, PrivateKeySource.Value, requestId);
                    handler.Go();
                    return new HttpStatusCodeResult(200);
                }
                else
                {
                    return new HttpStatusCodeResult(200);
                }
            }

            if (actionName.Equals("pull_request", StringComparison.InvariantCultureIgnoreCase))
            {
                PullRequestEventPayload pullPayload = RequestPayloadHelper.Parse<PullRequestEventPayload>(body);
                if (pullPayload.Action.Equals("opened", StringComparison.InvariantCultureIgnoreCase))
                {
                    CheckSuiteRequestHandler handler = new CheckSuiteRequestHandler(pullPayload, PrivateKeySource.Value, requestId);
                    handler.Go().Wait();
                   return new HttpStatusCodeResult(200);
                }
                else
                {
                    return new HttpStatusCodeResult(200);
                }
            }

           CheckSuiteEventPayload payload = RequestPayloadHelper.Parse(body);

            if (!payload.Action.Equals("rerequested", StringComparison.OrdinalIgnoreCase) &&
                (payload.CheckSuite.PullRequests == null || payload.CheckSuite.PullRequests.Count == 0))
            {
               return new HttpStatusCodeResult(200);
            }

            if (!payload.Action.Equals("completed", StringComparison.OrdinalIgnoreCase))
            {
                CheckSuiteRequestHandler handler = new CheckSuiteRequestHandler(payload, PrivateKeySource.Value, requestId);
                handler.Go();
            }

            return new HttpStatusCodeResult(200);
        }

        private static string GetRequestPostData(HttpRequestBase request)
        {
            if (request.HttpMethod != HttpMethod.Post.Method)
            {
                return null;
            }
            using (Stream body = request.InputStream)
            {
                using (StreamReader reader = new StreamReader(body, request.ContentEncoding))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
