// PotPlayer loads this extension and calls OnInitialize when it starts.
// The bootstrap returns immediately; the bridge follows the player lifetime.
void OnInitialize()
{
    string bridge = HostGetExecuteFolder() + "\\Extension\\BilibiliInput\\BilibiliInput.exe";
    if (HostFileExist(bridge)) HostExecuteProgram(bridge, "--from-player");
}

string GetTitle() { return "Bilibili Input"; }
string GetVersion() { return "1"; }
string GetDesc() { return "Local player input controls; no network access."; }
bool PlayitemCheck(const string &in path) { return false; }
bool PlaylistCheck(const string &in path) { return false; }
array<dictionary> PlayitemParse(const string &in path) { return array<dictionary>(); }
array<dictionary> PlaylistParse(const string &in path) { return array<dictionary>(); }
void PlayitemCancel() { }
void PlaylistCancel() { }
string GetStatus() { return ""; }
