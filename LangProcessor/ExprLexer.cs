namespace LangProcessor;

public sealed record ExprToken(int Code, string Type, string Text, int Line, int Column, int Offset)
{
    public int Length => Text.Length;
    public bool IsError => Code == ExprLexer.ErrorCode;
    public string CodeText => IsError ? "ERROR" : Code.ToString();
    public string Location => $"строка {Line}, позиция {Column}";
}

public enum LexerState
{
    Start = 0,
    Identifier = 1,
    Number = 2,
}

public static class ExprLexer
{
    public const int ErrorCode = 0;
    public const int Number = 1;
    public const int Identifier = 2;
    public const int Plus = 3;
    public const int Minus = 4;
    public const int Multiply = 5;
    public const int Divide = 6;
    public const int Modulo = 7;
    public const int LeftParen = 8;
    public const int RightParen = 9;

    private static readonly Dictionary<char, (int Code, string Type)> Operators = new()
    {
        ['+'] = (Plus, "оператор сложения"),
        ['-'] = (Minus, "оператор вычитания"),
        ['*'] = (Multiply, "оператор умножения"),
        ['/'] = (Divide, "оператор деления"),
        ['%'] = (Modulo, "оператор остатка от деления"),
        ['('] = (LeftParen, "открывающая скобка"),
        [')'] = (RightParen, "закрывающая скобка"),
    };

    private static bool IsLetter(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '_';
    private static bool IsDigit(char c) => c is >= '0' and <= '9';

    public static List<ExprToken> Analyze(string text)
    {
        var tokens = new List<ExprToken>();
        var state = LexerState.Start;
        int line = 1, lineStart = 0, start = 0, i = 0;

        void Emit(int code, string type, int end) =>
            tokens.Add(new ExprToken(code, type, text[start..end], line, start - lineStart + 1, start));

        while (true)
        {
            var c = i < text.Length ? text[i] : '\0';
            switch (state)
            {
                case LexerState.Start:
                    if (i >= text.Length) return tokens;
                    start = i;
                    if (c == '\n') { i++; line++; lineStart = i; }
                    else if (c is ' ' or '\t' or '\r') i++;
                    else if (IsLetter(c)) { state = LexerState.Identifier; i++; }
                    else if (IsDigit(c)) { state = LexerState.Number; i++; }
                    else if (Operators.TryGetValue(c, out var op)) { i++; Emit(op.Code, op.Type, i); }
                    else { i++; Emit(ErrorCode, "недопустимый символ", i); }
                    break;

                case LexerState.Identifier:
                    if (IsLetter(c) || IsDigit(c)) i++;
                    else { Emit(Identifier, "идентификатор", i); state = LexerState.Start; }
                    break;

                case LexerState.Number:
                    if (IsDigit(c)) i++;
                    else { Emit(Number, "целое число", i); state = LexerState.Start; }
                    break;
            }
        }
    }
}
