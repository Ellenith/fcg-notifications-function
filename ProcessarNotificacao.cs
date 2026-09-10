using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace fcg_notifications_function;

public record UserCreatedEventDto(string Email, string Nome, string Role, DateTime CriadoEm);
public record PaymentProcessedEventDto(Guid UsuarioId, Guid JogoId, decimal Preco, string OrderId, string Status, string? Mensagem);

public class NotificacaoFunctions
{
    private readonly ILogger<NotificacaoFunctions> _logger;

    public NotificacaoFunctions(ILogger<NotificacaoFunctions> logger)
        => _logger = logger;

    [Function("ProcessarUserCreated")]
    public async Task<HttpResponseData> ProcessarUserCreated(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "user-created")] HttpRequestData req)
    {
        var evento = await req.ReadFromJsonAsync<UserCreatedEventDto>();
        if (evento is null) return req.CreateResponse(HttpStatusCode.BadRequest);

        _logger.LogInformation(
            "📧 [E-MAIL BOAS-VINDAS SIMULADO]" +
            "\n  Para: {Email}" +
            "\n  Assunto: Bem-vindo à FIAP Cloud Games, {Nome}!" +
            "\n  Corpo: Olá {Nome}, sua conta foi criada com sucesso!" +
            "\n         Role: {Role} | Criado em: {CriadoEm}",
            evento.Email, evento.Nome, evento.Nome, evento.Role, evento.CriadoEm.ToLocalTime());

        return req.CreateResponse(HttpStatusCode.OK);
    }

    [Function("ProcessarPaymentProcessed")]
    public async Task<HttpResponseData> ProcessarPaymentProcessed(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "payment-processed")] HttpRequestData req)
    {
        var evento = await req.ReadFromJsonAsync<PaymentProcessedEventDto>();
        if (evento is null) return req.CreateResponse(HttpStatusCode.BadRequest);

        if (evento.Status == "Approved")
        {
            _logger.LogInformation(
                "📧 [E-MAIL CONFIRMAÇÃO DE COMPRA SIMULADO]" +
                "\n  Para: UsuarioId={UsuarioId}" +
                "\n  Assunto: Sua compra foi aprovada!" +
                "\n  Corpo: O jogo JogoId={JogoId} foi adicionado à sua biblioteca." +
                "\n         Valor pago: R${Preco:F2}" +
                "\n         OrderId: {OrderId}",
                evento.UsuarioId, evento.JogoId, evento.Preco, evento.OrderId);
        }
        else
        {
            _logger.LogWarning(
                "📧 [E-MAIL PAGAMENTO REJEITADO SIMULADO]" +
                "\n  Para: UsuarioId={UsuarioId}" +
                "\n  Assunto: Problema com seu pagamento" +
                "\n  Corpo: Infelizmente seu pagamento foi recusado." +
                "\n         Motivo: {Mensagem}" +
                "\n         OrderId: {OrderId}",
                evento.UsuarioId, evento.Mensagem, evento.OrderId);
        }

        return req.CreateResponse(HttpStatusCode.OK);
    }
}