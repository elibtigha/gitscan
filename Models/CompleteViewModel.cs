using System.Collections.Generic;
using Octokit;

namespace OctokitDemo.Models
{
    public class CompleteViewModel
    {
        public CompleteViewModel()
        {
            // Repositories = repositories;
        }

        public IEnumerable<Repository> Repositories { get; private set; }
    }
}
