using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BrothersCare.Api.Identity;
using BrothersCare.Application.Scheduling;
using BrothersCare.Application.Scheduling.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace BrothersCare.Api.Care;

public static class SchedulingEndpoints
{
    public static IEndpointRouteBuilder MapSchedulingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/appointments/options", GetOptionsAsync)
            .RequireAuthorization()
            .WithTags("Agenda")
            .WithName("GetSchedulingOptions")
            .WithSummary("Lista pacientes e unidades disponíveis para o profissional agendar.")
            .Produces<SchedulingOptions>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        endpoints.MapGet("/api/v1/appointments", ListAsync)
            .RequireAuthorization()
            .WithTags("Agenda")
            .WithName("ListAppointments")
            .WithSummary("Lista agendamentos autorizados no tenant em um intervalo obrigatório de até 93 dias.")
            .WithDescription("Profissionais consultam apenas a própria agenda; administradores da clínica podem filtrar por profissional e unidade. status aceita Scheduled, Completed, NoShow ou Cancelled. from e to são inclusivos e exigem offset.")
            .Produces<IReadOnlyList<AppointmentScheduleItem>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        endpoints.MapGet("/api/v1/appointments/{appointmentId:guid}", GetAppointmentAsync)
            .RequireAuthorization()
            .WithTags("Agenda")
            .WithName("GetAppointment")
            .WithSummary("Consulta um agendamento autorizado na empresa ativa.")
            .Produces<AppointmentScheduleItem>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        endpoints.MapPost("/api/v1/appointments", CreateAsync)
            .RequireAuthorization()
            .WithTags("Agenda")
            .WithName("CreateAppointment")
            .WithSummary("Agenda um atendimento para um paciente sob os cuidados do profissional.")
            .WithDescription("Tenant, profissional e membership são derivados da sessão. Horários devem incluir offset.")
            .Produces<AppointmentItem>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        endpoints.MapPatch("/api/v1/appointments/{appointmentId:guid}", RescheduleAsync)
            .RequireAuthorization()
            .WithTags("Agenda")
            .WithName("RescheduleAppointment")
            .WithSummary("Reagenda um atendimento agendado do profissional autenticado.")
            .WithDescription("Revalida unidade, horário e conflitos sem criar outro atendimento. Datas devem conter offset.")
            .Produces<AppointmentItem>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        endpoints.MapPost("/api/v1/appointments/{appointmentId:guid}/cancel", CancelAsync)
            .RequireAuthorization()
            .WithTags("Agenda")
            .WithName("CancelAppointment")
            .WithSummary("Cancela um atendimento agendado do profissional autenticado.")
            .WithDescription("Exige um motivo e preserva o registro do atendimento cancelado.")
            .Produces<AppointmentItem>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapPost("/api/v1/appointments/{appointmentId:guid}/complete", CompleteAsync)
            .RequireAuthorization()
            .WithTags("Agenda")
            .WithName("CompleteAppointment")
            .WithSummary("Marca um atendimento iniciado como realizado.")
            .WithDescription("Somente o profissional responsável pode concluir um atendimento após o horário de início.")
            .Produces<AppointmentItem>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapPost("/api/v1/appointments/{appointmentId:guid}/no-show", MarkNoShowAsync)
            .RequireAuthorization()
            .WithTags("Agenda")
            .WithName("MarkAppointmentNoShow")
            .WithSummary("Marca falta em um atendimento iniciado.")
            .WithDescription("Exige um motivo e somente o profissional responsável pode marcar a falta após o horário de início.")
            .Produces<AppointmentItem>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<Results<Ok<IReadOnlyList<AppointmentScheduleItem>>, ProblemHttpResult>> ListAsync(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? status,
        [FromQuery] Guid? professionalId,
        [FromQuery] Guid? clinicUnitId,
        [FromServices] ListAppointments listAppointments,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var query = context.Request.Query;
        if (!HasValidListQuery(query) || from is null || to is null ||
            !RequiredOffsetDateTimeOffsetJsonConverter.TryParse(query["from"].ToString(), out var parsedFrom) ||
            !RequiredOffsetDateTimeOffsetJsonConverter.TryParse(query["to"].ToString(), out var parsedTo))
        {
            return AuthenticationProblems.Create(
                context,
                StatusCodes.Status400BadRequest,
                "Informe from e to com offset e intervalo de até 93 dias.",
                "validation");
        }

        var result = await listAppointments.ExecuteAsync(
            parsedFrom, parsedTo, status, professionalId, clinicUnitId, cancellationToken);
        if (result.Status == AppointmentQueryStatus.Success)
        {
            var appointments = result.Appointments
                ?? throw new InvalidOperationException("A successful appointment list must contain its results.");
            return TypedResults.Ok(appointments);
        }

        return result.Status switch
        {
            AppointmentQueryStatus.Unauthenticated => AuthenticationProblems.Create(
                context,
                StatusCodes.Status401Unauthorized,
                "Sessão ausente, inválida ou expirada.",
                "authentication-required"),
            AppointmentQueryStatus.Forbidden => AuthenticationProblems.Create(
                context,
                StatusCodes.Status403Forbidden,
                "A agenda ou o filtro solicitado não está disponível nesta empresa.",
                "forbidden"),
            AppointmentQueryStatus.ProfessionalNotFound => AuthenticationProblems.Create(
                context,
                StatusCodes.Status404NotFound,
                "Perfil profissional não encontrado nesta empresa.",
                "professional-not-found"),
            AppointmentQueryStatus.InvalidInput => AuthenticationProblems.Create(
                context,
                StatusCodes.Status400BadRequest,
                "O período deve ter no máximo 93 dias, from não pode ser posterior a to e status deve ser válido.",
                "validation"),
            _ => throw new InvalidOperationException($"Unsupported appointment query status: {result.Status}."),
        };
    }

    private static async Task<Results<Ok<AppointmentScheduleItem>, ProblemHttpResult>> GetAppointmentAsync(
        Guid appointmentId,
        [FromServices] GetAppointment getAppointment,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var result = await getAppointment.ExecuteAsync(appointmentId, cancellationToken);
        if (result.Status == AppointmentQueryStatus.Success)
        {
            var appointment = result.Appointment
                ?? throw new InvalidOperationException("A successful appointment query must contain an appointment.");
            return TypedResults.Ok(appointment);
        }

        return result.Status switch
        {
            AppointmentQueryStatus.Unauthenticated => AuthenticationProblems.Create(
                context,
                StatusCodes.Status401Unauthorized,
                "Sessão ausente, inválida ou expirada.",
                "authentication-required"),
            AppointmentQueryStatus.Forbidden => AuthenticationProblems.Create(
                context,
                StatusCodes.Status403Forbidden,
                "Selecione uma empresa ativa para acessar a agenda.",
                "forbidden"),
            AppointmentQueryStatus.ProfessionalNotFound or
                AppointmentQueryStatus.NotFound => AuthenticationProblems.Create(
                context,
                StatusCodes.Status404NotFound,
                "Agendamento não encontrado.",
                "appointment-not-found"),
            _ => throw new InvalidOperationException($"Unsupported appointment query status: {result.Status}."),
        };
    }

    private static async Task<Results<Created<AppointmentItem>, ProblemHttpResult>> CreateAsync(
        [FromBody] CreateAppointmentRequest? request,
        [FromServices] CreateAppointment createAppointment,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var result = await createAppointment.ExecuteAsync(
            request is null
                ? new CreateAppointmentCommand(Guid.Empty, Guid.Empty, default, default, null)
                : new CreateAppointmentCommand(
                    request.PatientId,
                    request.ClinicUnitId,
                    request.StartsAt,
                    request.EndsAt,
                    request.Notes),
            cancellationToken);

        if (result.Status == CreateAppointmentStatus.Success)
        {
            var appointment = result.Appointment
                ?? throw new InvalidOperationException("A successful appointment result must contain an appointment.");
            return TypedResults.Created(
                $"/api/v1/appointments/{appointment.Id:D}",
                appointment);
        }

        return result.Status switch
        {
            CreateAppointmentStatus.Unauthenticated => AuthenticationProblems.Create(
                context,
                StatusCodes.Status401Unauthorized,
                "Sessão ausente, inválida ou expirada.",
                "authentication-required"),
            CreateAppointmentStatus.Forbidden or
                CreateAppointmentStatus.ClinicUnitNotAvailable or
                CreateAppointmentStatus.PatientNotAssigned => AuthenticationProblems.Create(
                context,
                StatusCodes.Status403Forbidden,
                "O paciente ou a unidade não está disponível para este profissional.",
                "forbidden"),
            CreateAppointmentStatus.ProfessionalNotFound => AuthenticationProblems.Create(
                context,
                StatusCodes.Status404NotFound,
                "Perfil profissional não encontrado nesta empresa.",
                "professional-not-found"),
            CreateAppointmentStatus.StartsInPast => AuthenticationProblems.Create(
                context,
                StatusCodes.Status422UnprocessableEntity,
                "O horário inicial precisa estar no presente ou no futuro.",
                "business-rule"),
            CreateAppointmentStatus.ProfessionalConflict or
                CreateAppointmentStatus.PatientConflict => AuthenticationProblems.Create(
                context,
                StatusCodes.Status409Conflict,
                "O horário conflita com outro agendamento ativo.",
                "appointment-conflict"),
            CreateAppointmentStatus.InvalidInput => AuthenticationProblems.Create(
                context,
                StatusCodes.Status400BadRequest,
                "Informe paciente, unidade e um intervalo de horário válido.",
                "validation"),
            _ => throw new InvalidOperationException($"Unsupported appointment creation status: {result.Status}."),
        };
    }

    private static async Task<Results<Ok<AppointmentItem>, ProblemHttpResult>> RescheduleAsync(
        Guid appointmentId,
        [FromBody] RescheduleAppointmentRequest? request,
        [FromServices] RescheduleAppointment rescheduleAppointment,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var result = await rescheduleAppointment.ExecuteAsync(
            request is null
                ? new RescheduleAppointmentCommand(appointmentId, Guid.Empty, default, default)
                : new RescheduleAppointmentCommand(
                    appointmentId,
                    request.ClinicUnitId,
                    request.StartsAt,
                    request.EndsAt),
            cancellationToken);

        if (result.Status == RescheduleAppointmentStatus.Success)
        {
            return TypedResults.Ok(result.Appointment
                ?? throw new InvalidOperationException("A successful reschedule result must contain an appointment."));
        }

        return result.Status switch
        {
            RescheduleAppointmentStatus.Unauthenticated => AuthenticationProblems.Create(
                context,
                StatusCodes.Status401Unauthorized,
                "Sessão ausente, inválida ou expirada.",
                "authentication-required"),
            RescheduleAppointmentStatus.Forbidden or
                RescheduleAppointmentStatus.ClinicUnitNotAvailable => AuthenticationProblems.Create(
                    context,
                    StatusCodes.Status403Forbidden,
                    "O agendamento ou a unidade não está disponível para este profissional.",
                    "forbidden"),
            RescheduleAppointmentStatus.ProfessionalNotFound or
                RescheduleAppointmentStatus.NotFound => AuthenticationProblems.Create(
                    context,
                    StatusCodes.Status404NotFound,
                    "Agendamento não encontrado.",
                    "appointment-not-found"),
            RescheduleAppointmentStatus.NotScheduled => AuthenticationProblems.Create(
                context,
                StatusCodes.Status409Conflict,
                "Somente atendimentos agendados podem ser reagendados.",
                "invalid-appointment-status"),
            RescheduleAppointmentStatus.StartsInPast => AuthenticationProblems.Create(
                context,
                StatusCodes.Status422UnprocessableEntity,
                "O novo horário inicial precisa estar no presente ou no futuro.",
                "business-rule"),
            RescheduleAppointmentStatus.ProfessionalConflict or
                RescheduleAppointmentStatus.PatientConflict => AuthenticationProblems.Create(
                    context,
                    StatusCodes.Status409Conflict,
                    "O novo horário conflita com outro agendamento ativo.",
                    "appointment-conflict"),
            RescheduleAppointmentStatus.InvalidInput => AuthenticationProblems.Create(
                context,
                StatusCodes.Status400BadRequest,
                "Informe unidade e um novo intervalo de horário válido.",
                "validation"),
            _ => throw new InvalidOperationException($"Unsupported appointment reschedule status: {result.Status}."),
        };
    }

    private static async Task<Results<Ok<AppointmentItem>, ProblemHttpResult>> CancelAsync(
        Guid appointmentId,
        [FromBody] CancelAppointmentRequest? request,
        [FromServices] CancelAppointment cancelAppointment,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var result = await cancelAppointment.ExecuteAsync(
            new CancelAppointmentCommand(appointmentId, request?.Reason),
            cancellationToken);

        if (result.Status == CancelAppointmentStatus.Success)
        {
            return TypedResults.Ok(result.Appointment
                ?? throw new InvalidOperationException("A successful cancellation result must contain an appointment."));
        }

        return result.Status switch
        {
            CancelAppointmentStatus.Unauthenticated => AuthenticationProblems.Create(
                context,
                StatusCodes.Status401Unauthorized,
                "Sessão ausente, inválida ou expirada.",
                "authentication-required"),
            CancelAppointmentStatus.Forbidden => AuthenticationProblems.Create(
                context,
                StatusCodes.Status403Forbidden,
                "O agendamento não está disponível para este profissional.",
                "forbidden"),
            CancelAppointmentStatus.ProfessionalNotFound or
                CancelAppointmentStatus.NotFound => AuthenticationProblems.Create(
                context,
                StatusCodes.Status404NotFound,
                "Agendamento não encontrado.",
                "appointment-not-found"),
            CancelAppointmentStatus.NotScheduled => AuthenticationProblems.Create(
                context,
                StatusCodes.Status409Conflict,
                "Somente atendimentos agendados podem ser cancelados.",
                "invalid-appointment-status"),
            CancelAppointmentStatus.InvalidInput => AuthenticationProblems.Create(
                context,
                StatusCodes.Status400BadRequest,
                "Informe um motivo de cancelamento com até 500 caracteres.",
                "validation"),
            _ => throw new InvalidOperationException($"Unsupported appointment cancellation status: {result.Status}."),
        };
    }

    private static async Task<Results<Ok<AppointmentItem>, ProblemHttpResult>> CompleteAsync(
        Guid appointmentId,
        [FromServices] CompleteAppointment completeAppointment,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var result = await completeAppointment.ExecuteAsync(
            new CompleteAppointmentCommand(appointmentId), cancellationToken);
        if (result.Status == CompleteAppointmentStatus.Success)
        {
            return TypedResults.Ok(result.Appointment
                ?? throw new InvalidOperationException("A successful completion result must contain an appointment."));
        }

        return result.Status switch
        {
            CompleteAppointmentStatus.Unauthenticated => AuthenticationProblems.Create(context, StatusCodes.Status401Unauthorized, "Sessão ausente, inválida ou expirada.", "authentication-required"),
            CompleteAppointmentStatus.Forbidden => AuthenticationProblems.Create(context, StatusCodes.Status403Forbidden, "O agendamento não está disponível para este profissional.", "forbidden"),
            CompleteAppointmentStatus.ProfessionalNotFound or CompleteAppointmentStatus.NotFound => AuthenticationProblems.Create(context, StatusCodes.Status404NotFound, "Agendamento não encontrado.", "appointment-not-found"),
            CompleteAppointmentStatus.NotScheduled => AuthenticationProblems.Create(context, StatusCodes.Status409Conflict, "Somente atendimentos agendados podem ser marcados como realizados.", "invalid-appointment-status"),
            CompleteAppointmentStatus.NotStarted => AuthenticationProblems.Create(context, StatusCodes.Status409Conflict, "O atendimento só pode ser marcado como realizado após o horário de início.", "appointment-not-started"),
            CompleteAppointmentStatus.InvalidInput => AuthenticationProblems.Create(context, StatusCodes.Status400BadRequest, "Informe um agendamento válido.", "validation"),
            _ => throw new InvalidOperationException($"Unsupported appointment completion status: {result.Status}."),
        };
    }

    private static async Task<Results<Ok<AppointmentItem>, ProblemHttpResult>> MarkNoShowAsync(
        Guid appointmentId,
        [FromBody] MarkAppointmentNoShowRequest? request,
        [FromServices] MarkAppointmentNoShow markAppointmentNoShow,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var result = await markAppointmentNoShow.ExecuteAsync(
            new MarkAppointmentNoShowCommand(appointmentId, request?.Reason), cancellationToken);
        if (result.Status == MarkAppointmentNoShowStatus.Success)
        {
            return TypedResults.Ok(result.Appointment
                ?? throw new InvalidOperationException("A successful no-show result must contain an appointment."));
        }

        return result.Status switch
        {
            MarkAppointmentNoShowStatus.Unauthenticated => AuthenticationProblems.Create(context, StatusCodes.Status401Unauthorized, "Sessão ausente, inválida ou expirada.", "authentication-required"),
            MarkAppointmentNoShowStatus.Forbidden => AuthenticationProblems.Create(context, StatusCodes.Status403Forbidden, "O agendamento não está disponível para este profissional.", "forbidden"),
            MarkAppointmentNoShowStatus.ProfessionalNotFound or MarkAppointmentNoShowStatus.NotFound => AuthenticationProblems.Create(context, StatusCodes.Status404NotFound, "Agendamento não encontrado.", "appointment-not-found"),
            MarkAppointmentNoShowStatus.NotScheduled => AuthenticationProblems.Create(context, StatusCodes.Status409Conflict, "Somente atendimentos agendados podem ser marcados como falta.", "invalid-appointment-status"),
            MarkAppointmentNoShowStatus.NotStarted => AuthenticationProblems.Create(context, StatusCodes.Status409Conflict, "A falta só pode ser registrada após o horário de início.", "appointment-not-started"),
            MarkAppointmentNoShowStatus.InvalidInput => AuthenticationProblems.Create(context, StatusCodes.Status400BadRequest, "Informe um motivo de falta com até 500 caracteres.", "validation"),
            _ => throw new InvalidOperationException($"Unsupported no-show status: {result.Status}."),
        };
    }

    private static async Task<Results<Ok<SchedulingOptions>, ProblemHttpResult>> GetOptionsAsync(
        [FromServices] GetSchedulingOptions getSchedulingOptions,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var result = await getSchedulingOptions.ExecuteAsync(cancellationToken);
        if (result.Status == SchedulingOptionsStatus.Success && result.Options is not null)
        {
            return TypedResults.Ok(result.Options);
        }

        return result.Status switch
        {
            SchedulingOptionsStatus.Unauthenticated => AuthenticationProblems.Create(
                context,
                StatusCodes.Status401Unauthorized,
                "Sessão ausente, inválida ou expirada.",
                "authentication-required"),
            SchedulingOptionsStatus.Forbidden => AuthenticationProblems.Create(
                context,
                StatusCodes.Status403Forbidden,
                "Selecione uma empresa ativa para acessar a agenda.",
                "forbidden"),
            _ => AuthenticationProblems.Create(
                context,
                StatusCodes.Status404NotFound,
                "Perfil profissional não encontrado nesta empresa.",
                "professional-not-found"),
        };
    }

    private static bool HasValidListQuery(IQueryCollection query) =>
        query.Keys.All(key =>
            key.Equals("from", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("to", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("status", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("professionalId", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("clinicUnitId", StringComparison.OrdinalIgnoreCase)) &&
        query["from"].Count == 1 &&
        query["to"].Count == 1 &&
        query["status"].Count <= 1 &&
        query["professionalId"].Count <= 1 &&
        query["clinicUnitId"].Count <= 1;

}

/// <summary>Dados para agendar um atendimento sem informar tenant ou profissional.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateAppointmentRequest(
    Guid PatientId,
    Guid ClinicUnitId,
    [property: JsonConverter(typeof(RequiredOffsetDateTimeOffsetJsonConverter))]
    DateTimeOffset StartsAt,
    [property: JsonConverter(typeof(RequiredOffsetDateTimeOffsetJsonConverter))]
    DateTimeOffset EndsAt,
    string? Notes);

/// <summary>Novo local e horário de um atendimento agendado.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RescheduleAppointmentRequest(
    Guid ClinicUnitId,
    [property: JsonConverter(typeof(RequiredOffsetDateTimeOffsetJsonConverter))]
    DateTimeOffset StartsAt,
    [property: JsonConverter(typeof(RequiredOffsetDateTimeOffsetJsonConverter))]
    DateTimeOffset EndsAt);

/// <summary>Motivo obrigatório para cancelar um atendimento agendado.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CancelAppointmentRequest(string? Reason);

/// <summary>Motivo obrigatório para registrar a falta do paciente.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MarkAppointmentNoShowRequest(string? Reason);

public sealed class RequiredOffsetDateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset>
{
    private static readonly string[] Formats =
    [
        "yyyy-MM-dd'T'HH:mm:sszzz",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
    ];

    public override DateTimeOffset Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Date-time values must be strings with an explicit UTC offset.");
        }

        if (!TryParse(reader.GetString(), out var parsed))
        {
            throw new JsonException("Date-time values must use ISO 8601 with an explicit UTC offset.");
        }

        return parsed;
    }

    public static bool TryParse(string? value, out DateTimeOffset parsed)
    {
        parsed = default;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var normalizedValue = value.EndsWith("Z", StringComparison.OrdinalIgnoreCase)
            ? value[..^1] + "+00:00"
            : value;
        return DateTimeOffset.TryParseExact(
            normalizedValue,
            Formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out parsed);
    }

    public override void Write(
        Utf8JsonWriter writer,
        DateTimeOffset value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
