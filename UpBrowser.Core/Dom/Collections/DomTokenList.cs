using System.Collections;

namespace UpBrowser.Core.Dom;

public class DomTokenList : IReadOnlyList<string>, IEnumerable<string>
{
    private readonly Func<string> _getValue;
    private readonly Action<string> _setValue;
    private string[] _tokens = Array.Empty<string>();

    public DomTokenList(Func<string> getValue, Action<string> setValue)
    {
        _getValue = getValue;
        _setValue = setValue;
        UpdateTokens();
    }

    public int Length => _tokens.Length;
    public int Count => _tokens.Length;

    public string Value
    {
        get => _getValue();
        set
        {
            _setValue(value);
            UpdateTokens();
        }
    }

    public bool Contains(string token)
    {
        return _tokens.Contains(token);
    }

    public void Add(params string[] tokens)
    {
        var set = new HashSet<string>(_tokens);
        bool changed = false;
        foreach (var t in tokens)
        {
            if (string.IsNullOrEmpty(t)) continue;
            if (set.Add(t)) changed = true;
        }
        if (changed)
        {
            _tokens = set.ToArray();
            SyncValue();
        }
    }

    public void Remove(params string[] tokens)
    {
        var set = new HashSet<string>(_tokens);
        bool changed = false;
        foreach (var t in tokens)
        {
            if (set.Remove(t)) changed = true;
        }
        if (changed)
        {
            _tokens = set.ToArray();
            SyncValue();
        }
    }

    public bool Toggle(string token, bool? force = null)
    {
        if (string.IsNullOrEmpty(token)) return false;
        if (force == true)
        {
            Add(token);
            return true;
        }
        if (force == false)
        {
            Remove(token);
            return false;
        }
        if (Contains(token))
        {
            Remove(token);
            return false;
        }
        Add(token);
        return true;
    }

    public bool Replace(string oldToken, string newToken)
    {
        if (string.IsNullOrEmpty(oldToken) || string.IsNullOrEmpty(newToken)) return false;
        if (!Contains(oldToken)) return false;
        var list = _tokens.ToList();
        int idx = list.IndexOf(oldToken);
        if (idx >= 0)
        {
            list[idx] = newToken;
            _tokens = list.ToArray();
            SyncValue();
            return true;
        }
        return false;
    }

    public bool Supports(string token) => true;

    public string this[int index] => _tokens[index];

    private void UpdateTokens()
    {
        var val = _getValue();
        _tokens = string.IsNullOrWhiteSpace(val)
            ? Array.Empty<string>()
            : val.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private void SyncValue()
    {
        _setValue(string.Join(" ", _tokens));
    }

    public IEnumerator<string> GetEnumerator() => ((IEnumerable<string>)_tokens).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _tokens.GetEnumerator();
}
