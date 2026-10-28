namespace DynamicIsland.Windows.Infrastructure.CommandPalette;

// Full command text lives only in this app session; questions and arguments are not written to disk.
public sealed class PaletteCommandHistory
{
    private readonly List<string> _entries=[];
    private int _index=-1;
    private string _draft="";
    public bool IsBrowsing => _index>=0;
    public void Remember(string query)
    {
        StopBrowsing();
        if(string.IsNullOrWhiteSpace(query) || (_entries.Count>0 && _entries[0]==query))return;
        _entries.Insert(0,query);if(_entries.Count>50)_entries.RemoveAt(50);
    }
    public bool Previous(string draft,out string query)
    {
        query=draft;if(_entries.Count==0)return false;
        if(_index<0){_draft=draft;_index=0;}else _index=Math.Min(_index+1,_entries.Count-1);
        query=_entries[_index];return true;
    }
    public bool Next(out string query)
    {
        query="";if(_index<0)return false;
        if(_index==0){query=_draft;StopBrowsing();}else query=_entries[--_index];
        return true;
    }
    public void StopBrowsing(){_index=-1;_draft="";}
}
