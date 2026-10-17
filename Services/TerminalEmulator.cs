using System.Text;

namespace MoxTerm.Services;

/// <summary>
/// 极简 VT/ANSI 屏幕缓冲区：把 ConPTY 输出的字符流解析成固定尺寸的字符网格。
/// <para>
/// 实现的是交互式 Shell 真正会用到的那部分控制序列：光标移动/定位、行内与全屏擦除、
/// 换行回车退格、制表符、光标显隐。SGR 颜色序列会被识别并跳过（暂不渲染颜色）。
/// 之所以需要它而不是简单地把转义序列删掉，是因为 Shell 刷新提示符时依赖
/// “定位光标 + 擦除行”来重绘，直接删掉转义序列会得到错乱甚至重复的输出。
/// </para>
/// </summary>
public sealed class TerminalEmulator
{
    private const char Blank = ' ';


    private enum State
    {
        Normal,
        Escape,
        ControlSequence,
        ControlString,
        ControlStringEscape,
    }

    private readonly StringBuilder _parameters = new(32);
    private readonly List<string> _scrollback = new();

    private State _state = State.Normal;
    private char[] _cells = [];
    private int _columns;
    private int _rows;
    private int _cursorX;
    private int _cursorY;
    private bool _cursorVisible = true;
    private int _maxScrollback = 2000;

    public TerminalEmulator(int columns = 80, int rows = 24)
    {
        Resize(columns, rows);
    }

    public int Columns => _columns;

    public int Rows => _rows;

    /// <summary>回滚缓冲的最大行数，超出后丢弃最旧的行。</summary>
    public int MaxScrollback
    {
        get => _maxScrollback;
        set
        {
            _maxScrollback = Math.Max(0, value);
            TrimScrollback();
        }
    }

    /// <summary>渲染光标时插入的字符，由光标形状设置决定。</summary>
    public char CursorGlyph { get; set; } = '\u2588';


    /// <summary>改变屏幕尺寸。已有内容按左上角对齐保留，Shell 随后会重绘。</summary>
    public void Resize(int columns, int rows)
    {
        columns = Math.Max(1, columns);
        rows = Math.Max(1, rows);

        if (columns == _columns && rows == _rows)
        {
            return;
        }

        var next = new char[columns * rows];
        Array.Fill(next, Blank);

        if (_cells.Length > 0)
        {
            var copyRows = Math.Min(rows, _rows);
            var copyColumns = Math.Min(columns, _columns);

            for (var y = 0; y < copyRows; y++)
            {
                Array.Copy(_cells, y * _columns, next, y * columns, copyColumns);
            }
        }

        _cells = next;
        _columns = columns;
        _rows = rows;
        _cursorX = Math.Clamp(_cursorX, 0, columns - 1);
        _cursorY = Math.Clamp(_cursorY, 0, rows - 1);
    }

    /// <summary>清空屏幕与回滚缓冲（等价于终端的 cls）。</summary>
    public void Clear()
    {
        Array.Fill(_cells, Blank);
        _scrollback.Clear();
        _cursorX = 0;
        _cursorY = 0;
        _state = State.Normal;
        _parameters.Clear();
    }

    /// <summary>
    /// 把当前屏幕内容（含回滚缓冲）渲染成纯文本。
    /// </summary>
    /// <param name="showCursor">
    /// 是否画出光标。<c>false</c> 用于光标闪烁的“熄灭”相位，此时光标位置仍按原样渲染。
    /// </param>
    public string GetText(bool showCursor = true)
    {
        var builder = new StringBuilder((_scrollback.Count + _rows) * (_columns + 1));

        foreach (var line in _scrollback)
        {
            builder.Append(line).Append('\n');
        }

        // 去掉底部没有内容的空行，避免界面下方出现大片空白
        var lastContentRow = _rows - 1;
        while (lastContentRow > 0 && IsRowBlank(lastContentRow))
        {
            lastContentRow--;
        }

        for (var y = 0; y <= lastContentRow; y++)
        {
            if (y == _cursorY && _cursorVisible && showCursor)
            {
                AppendRowWithCursor(builder, y);
            }
            else
            {
                AppendRow(builder, y);
            }

            builder.Append('\n');
        }

        return builder.ToString().TrimEnd('\n');
    }

    /// <summary>逐字符喂入 Shell 输出。允许在任意位置分块，解析状态会跨调用保持。</summary>
    public void Feed(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        foreach (var character in text)
        {
            FeedCharacter(character);
        }
    }

    private void FeedCharacter(char character)
    {
        switch (_state)
        {
            case State.Normal:
                if (character == '\u001b')
                {
                    _state = State.Escape;
                    return;
                }

                HandleCharacter(character);
                return;

            case State.Escape:
                switch (character)
                {
                    case '[':
                        _parameters.Clear();
                        _state = State.ControlSequence;
                        return;

                    case ']':
                    case 'P':
                    case 'X':
                    case '^':
                    case '_':
                        // OSC / DCS / SOS / PM / APC：整段丢弃
                        _state = State.ControlString;
                        return;

                    default:
                        // ESC 后跟单个字符的序列（如 ESC ( B、ESC =、ESC >）一律忽略
                        _state = State.Normal;
                        return;
                }

            case State.ControlSequence:
                if (character is >= '\u0040' and <= '\u007e')
                {
                    ExecuteControlSequence(character);
                    _state = State.Normal;
                    return;
                }

                if (_parameters.Length < 64)
                {
                    _parameters.Append(character);
                }

                return;

            case State.ControlString:
                if (character == '\a')
                {
                    _state = State.Normal;
                }
                else if (character == '\u001b')
                {
                    _state = State.ControlStringEscape;
                }

                return;

            case State.ControlStringEscape:
                _state = character == '\\' ? State.Normal : State.ControlString;
                return;
        }
    }

    private void HandleCharacter(char character)
    {
        switch (character)
        {
            case '\r':
                _cursorX = 0;
                return;

            case '\n':
                LineFeed();
                return;

            case '\b':
                if (_cursorX > 0)
                {
                    _cursorX--;
                }

                return;

            case '\t':
                _cursorX = Math.Min(_columns - 1, ((_cursorX / 8) + 1) * 8);
                return;

            case '\a':
            case '\0':
                return;
        }

        if (char.IsControl(character))
        {
            return;
        }

        // 延后换行：写在最后一列后先停在原地，下一个字符才换行
        if (_cursorX >= _columns)
        {
            _cursorX = 0;
            LineFeed();
        }

        _cells[(_cursorY * _columns) + _cursorX] = character;
        _cursorX++;
    }

    private void ExecuteControlSequence(char final)
    {
        var raw = _parameters.ToString();
        var isPrivate = raw.StartsWith('?');

        if (isPrivate)
        {
            raw = raw[1..];
        }

        Span<int> arguments = stackalloc int[8];
        var count = ParseArguments(raw, arguments);

        switch (final)

        {
            case 'A':
                MoveCursor(0, -Argument(arguments, count, 0, 1));
                break;

            case 'B':
                MoveCursor(0, Argument(arguments, count, 0, 1));
                break;

            case 'C':
                MoveCursor(Argument(arguments, count, 0, 1), 0);
                break;

            case 'D':
                MoveCursor(-Argument(arguments, count, 0, 1), 0);
                break;

            case 'E':
                _cursorX = 0;
                MoveCursor(0, Argument(arguments, count, 0, 1));
                break;

            case 'F':
                _cursorX = 0;
                MoveCursor(0, -Argument(arguments, count, 0, 1));
                break;

            case 'G':
                _cursorX = Math.Clamp(Argument(arguments, count, 0, 1) - 1, 0, _columns - 1);
                break;

            case 'd':
                _cursorY = Math.Clamp(Argument(arguments, count, 0, 1) - 1, 0, _rows - 1);
                break;

            case 'H':
            case 'f':
                _cursorY = Math.Clamp(Argument(arguments, count, 0, 1) - 1, 0, _rows - 1);
                _cursorX = Math.Clamp(Argument(arguments, count, 1, 1) - 1, 0, _columns - 1);
                break;

            case 'J':
                EraseDisplay(Argument(arguments, count, 0, 0));
                break;

            case 'K':
                EraseLine(Argument(arguments, count, 0, 0));
                break;

            case 'h':
                if (isPrivate && raw == "25")
                {
                    _cursorVisible = true;
                }

                break;

            case 'l':
                if (isPrivate && raw == "25")
                {
                    _cursorVisible = false;
                }

                break;

            // 'm'（SGR 颜色）等其余序列暂不渲染，直接忽略
            default:
                break;
        }
    }

    /// <summary>取第 <paramref name="index"/> 个 CSI 参数；缺省或为 0 时返回 <paramref name="fallback"/>。</summary>
    private static int Argument(ReadOnlySpan<int> arguments, int count, int index, int fallback)
    {
        if (index >= count)
        {
            return fallback;
        }

        var value = arguments[index];
        return value > 0 ? value : fallback;
    }

    private static int ParseArguments(string raw, Span<int> arguments)
    {
        var count = 0;
        var value = 0;
        var hasValue = false;

        foreach (var character in raw)
        {
            if (character is >= '0' and <= '9')
            {
                value = Math.Min(9999, (value * 10) + (character - '0'));
                hasValue = true;
            }
            else if (character == ';')
            {
                if (count < arguments.Length)
                {
                    arguments[count++] = hasValue ? value : 0;
                }

                value = 0;
                hasValue = false;
            }
        }

        if (count < arguments.Length)
        {
            arguments[count++] = hasValue ? value : 0;
        }

        return count;
    }

    private void MoveCursor(int deltaX, int deltaY)
    {
        _cursorX = Math.Clamp(_cursorX + deltaX, 0, _columns - 1);
        _cursorY = Math.Clamp(_cursorY + deltaY, 0, _rows - 1);
    }

    private void LineFeed()
    {
        _cursorY++;

        if (_cursorY >= _rows)
        {
            ScrollUp();
            _cursorY = _rows - 1;
        }
    }

    private void ScrollUp()
    {
        _scrollback.Add(GetRowText(0));
        TrimScrollback();

        Array.Copy(_cells, _columns, _cells, 0, (_rows - 1) * _columns);
        Array.Fill(_cells, Blank, (_rows - 1) * _columns, _columns);
    }

    private void TrimScrollback()
    {
        var excess = _scrollback.Count - _maxScrollback;
        if (excess > 0)
        {
            _scrollback.RemoveRange(0, excess);
        }
    }

    private void EraseLine(int mode)
    {
        var start = mode switch
        {
            0 => _cursorX,
            1 => 0,
            _ => 0,
        };

        var end = mode switch
        {
            0 => _columns,
            1 => Math.Min(_cursorX + 1, _columns),
            _ => _columns,
        };

        if (end > start)
        {
            Array.Fill(_cells, Blank, (_cursorY * _columns) + start, end - start);
        }
    }

    private void EraseDisplay(int mode)
    {
        var cursorIndex = (_cursorY * _columns) + _cursorX;

        switch (mode)
        {
            // 光标到屏幕末尾
            case 0:
                EraseRange(cursorIndex, _cells.Length);
                break;

            // 屏幕开头到光标
            case 1:
                EraseRange(0, cursorIndex + 1);
                break;

            // 整屏
            default:
                Array.Fill(_cells, Blank);
                break;
        }
    }

    private void EraseRange(int from, int to)
    {
        if (to > from)
        {
            Array.Fill(_cells, Blank, from, to - from);
        }
    }

    private bool IsRowBlank(int row)
    {
        var start = row * _columns;

        for (var x = 0; x < _columns; x++)
        {
            if (_cells[start + x] != Blank)
            {
                return false;
            }
        }

        return true;
    }

    private string GetRowText(int row)
    {
        var start = row * _columns;
        var end = start + _columns;

        while (end > start && _cells[end - 1] == Blank)
        {
            end--;
        }

        return new string(_cells, start, end - start);
    }

    private void AppendRow(StringBuilder builder, int row) => builder.Append(GetRowText(row));

    private void AppendRowWithCursor(StringBuilder builder, int row)
    {
        var text = GetRowText(row);

        // 光标块是插入而不是覆盖：只读视图里光标下的字符不该被吞掉
        if (_cursorX > text.Length)
        {
            builder.Append(text).Append(Blank, _cursorX - text.Length).Append(CursorGlyph);
            return;
        }

        builder.Append(text, 0, _cursorX)
            .Append(CursorGlyph)
            .Append(text, _cursorX, text.Length - _cursorX);
    }
}
