namespace Marina.Application.Common;
public sealed class ValidationException(IReadOnlyDictionary<string,string[]> errors):Exception("One or more validation errors occurred.") { public IReadOnlyDictionary<string,string[]> Errors { get; }=errors; }
