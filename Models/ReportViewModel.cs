using System.Collections.Generic;
using MS.Ess.Scan.DataContracts.Private;
using Octokit;

namespace OctokitDemo.Models
{
    public class ReportViewModel
    {
        public ReportViewModel(MalwareDeterminationResult result)
        {
            Result = result;
        }

        public MalwareDeterminationResult Result { get; private set; }
    }
}
