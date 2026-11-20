using Chameleon.App.Bridge;
using Chameleon.App.Bridge.Dto;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Chameleon.App.Tests.Bridge
{
    public class BalanceDtoJsonTests
    {
        [Fact]
        public void KeysWithoutALimit_SendRemainingAndTotalAsNull_NotLeaveThemOut()
        {
            // The UI tells "no limit" from a number; a missing field used to render as "NaN".
            JObject json = JObject.Parse(BridgeJson.Serialize(new BalanceDto { Used = 0.5m }));

            Assert.Equal(JTokenType.Null, json["remaining"]!.Type);
            Assert.Equal(JTokenType.Null, json["total"]!.Type);
            Assert.Equal(0.5m, (decimal)json["used"]!);
        }
    }
}
