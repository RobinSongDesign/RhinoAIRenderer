using System.Threading;
using System.Threading.Tasks;

namespace Chameleon.Core.Providers.Accounts
{
    /// <summary>Signs in to a relay gateway and obtains a key of the account for the plug-in.</summary>
    public interface IRelayAccountClient
    {
        /// <summary>Signs in with username and password.</summary>
        /// <param name="profile">Provider whose <see cref="ProviderProfile.SignInApi"/> says how.</param>
        /// <param name="username">Account name or e-mail.</param>
        /// <param name="password">Password; used for this request only, never stored.</param>
        /// <param name="keyName">Name of the key created for this installation, reused on later sign-ins.</param>
        /// <param name="cancellationToken">Cancels the requests.</param>
        /// <exception cref="Generation.RenderException">Wrong credentials, a disabled account, or an unreachable gateway.</exception>
        Task<RelaySignInResult> SignInAsync(ProviderProfile profile, string username, string password, string keyName, CancellationToken cancellationToken);

        /// <summary>Completes a sign-in that asked for a two-factor code.</summary>
        Task<RelaySignInResult> VerifyTwoFactorAsync(ProviderProfile profile, string flowToken, string code, string keyName, CancellationToken cancellationToken);
    }
}
