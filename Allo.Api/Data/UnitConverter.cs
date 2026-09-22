using Allo.Shared.Models;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Allo.Api.Data;

// Stores Unit as its lowercase code ("kg"), matching the JSON representation.
public class UnitConverter() : ValueConverter<Unit, string>(
    unit => unit.ToCode(),
    code => Units.FromCode(code));
