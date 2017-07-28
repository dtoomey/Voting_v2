using System.Collections.Generic;
using System.Threading.Tasks;

namespace VotingDataService
{
    public interface IVotingDataService : Microsoft.ServiceFabric.Services.Remoting.IService
    {
        Task<int> AddVote(string voteItem);

        Task<int> DeleteVoteItem(string voteItem);

        Task<int> GetNumberOfVotes(string voteItem);

        Task<long> GetTotalBallotsCast();

        Task<List<KeyValuePair<string, int>>> GetAllVoteCounts();
    }
}
