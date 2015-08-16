using System.Runtime.Serialization;

namespace Lpgist
{
    [DataContract]
    public class Config
    {
        [DataMember]
        public string LprunPath = "lprun.exe";

        [DataMember]
        public string Format = "html";

        [DataMember]
        public string GitHubAccessToken;

        [DataMember]
        public bool IsPublic;
    }
}
