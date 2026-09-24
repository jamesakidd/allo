using Allo.Shared.Models;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Allo.Api.Data;

// Stores Priority as its lowercase code ("high"), matching the JSON representation.
public class PriorityConverter() : ValueConverter<Priority, string>(
    priority => priority.ToCode(),
    code => Priorities.FromCode(code));
