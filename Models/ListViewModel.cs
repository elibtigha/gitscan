using System.Collections.Generic;
using Octokit;

namespace OctokitDemo.Models
{
    public class ListViewModel
    {
        public ListViewModel(IEnumerable<Repository> repositories)
        {
            Repositories = repositories;
        }

        public IEnumerable<Repository> Repositories { get; private set; }
    }
}
