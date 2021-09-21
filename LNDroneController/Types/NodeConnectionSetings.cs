namespace LNDroneController.Types
{
    public class NodeConnectionSetings
    {
        public string TlsCertFilePath {get;set;}
        public string MacaroonFilePath {get;set;}
        public string Host {get;set;}
        public string LocalIP { get; internal set; }
    }

}
