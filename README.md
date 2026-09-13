# FCG — Notifications Function

Função serverless que processa as notificações da plataforma FIAP Cloud Games. Substitui o processamento que antes rodava no `NotificationsAPI` como um serviço sempre ativo.

Parte do Tech Challenge — Fase 3. O repositório de orquestração, com a documentação central do projeto, está em `fcg-orchestration`.

---

## Por que serverless

O `NotificationsAPI` da Fase 2 era um consumidor de fila sempre ligado, mas notificações são eventos esporádicos — ele passava a maior parte do tempo ocioso, consumindo memória e CPU para esperar. O modelo FaaS inverte isso: não há processo em execução até que um evento chegue, e o custo acompanha o uso real.

---

## Arquitetura

```
   RabbitMQ ──▶ NotificationsAPI (adaptador) ──HTTP──▶ Azure Function
   (evento)      consome a fila e encaminha            processa e registra
```

A Function expõe dois endpoints HTTP, um por tipo de evento:

| Endpoint | Evento de origem | O que faz |
|---|---|---|
| `POST /api/user-created` | `UserCreatedEvent` | Registra o e-mail de boas-vindas |
| `POST /api/payment-processed` | `PaymentProcessedEvent` | Registra confirmação ou recusa da compra |

O envio de e-mail é simulado via log estruturado — integrar SendGrid, MailKit ou similar exigiria apenas substituir a chamada de log pela do provedor.

---

## A decisão do trigger

O caminho natural seria acionar a Function diretamente pela fila. A documentação atual dos dois principais provedores mostra por que isso não foi viável sem custo recorrente:

- **AWS Lambda** não aceita RabbitMQ self-hosted como gatilho nativo — exige Amazon MQ, o serviço gerenciado.
- **Azure Functions** tem trigger nativo de RabbitMQ, mas ele é suportado apenas nos planos Premium e Dedicated. O plano Consumption (gratuito, cobrando apenas pela execução) não o suporta.

**A escolha foi trigger HTTP no plano Consumption**, com o `NotificationsAPI` reduzido a um adaptador: ele continua consumindo a fila via MassTransit, mas em vez de processar a notificação, serializa o evento e invoca esta Function por HTTP.

É um meio-termo consciente. A parte sempre ligada não desaparece, mas fica reduzida a um encaminhador simples, e o processamento efetivo ocorre em ambiente serverless sem custo recorrente — prioridade em um projeto acadêmico.

Um efeito colateral favorável: como é o adaptador que faz a chamada de saída para a nuvem, o RabbitMQ não precisa ser exposto publicamente nem hospedado em serviço gerenciado.

---

## Executando localmente

**Pré-requisitos:** .NET SDK, Azure Functions Core Tools v4, Azurite (emulador de storage).

```bash
# terminal 1 — emulador de storage
azurite --silent --location ./azurite-data

# terminal 2 — a Function
func start
```

A Function sobe em `http://localhost:7071`. Teste com:

```bash
curl -i http://localhost:7071/api/user-created \
  -X POST -H "Content-Type: application/json" \
  -d '{"email":"teste@exemplo.com","nome":"Teste","role":"User","criadoEm":"2026-09-12T10:00:00Z"}'

curl -i http://localhost:7071/api/payment-processed \
  -X POST -H "Content-Type: application/json" \
  -d '{"usuarioId":"11111111-1111-1111-1111-111111111111","jogoId":"22222222-2222-2222-2222-222222222222","preco":49.90,"orderId":"abc123","status":"Approved"}'
```

O log formatado da notificação aparece no terminal do `func start`.

> O Azurite é necessário mesmo para Functions exclusivamente HTTP — o host do Functions valida o acesso ao storage durante a inicialização e falha o health check sem ele.

---

## Deploy no Azure

Criação dos recursos:

```bash
az login
az group create --name fcg-rg --location brazilsouth

az storage account create \
  --name <nome-do-storage> \
  --location brazilsouth \
  --resource-group fcg-rg \
  --sku Standard_LRS

az functionapp create \
  --resource-group fcg-rg \
  --consumption-plan-location brazilsouth \
  --runtime dotnet-isolated \
  --runtime-version 10 \
  --functions-version 4 \
  --name <nome-da-function-app> \
  --storage-account <nome-do-storage>
```

> O nome do Function App precisa ser único globalmente no Azure, pois compõe a URL pública.

Publicação:

```bash
func azure functionapp publish <nome-da-function-app>
```

Obtenção das chaves de acesso (os endpoints usam `AuthorizationLevel.Function`):

```bash
az functionapp function keys list \
  --resource-group fcg-rg \
  --name <nome-da-function-app> \
  --function-name ProcessarUserCreated

az functionapp function keys list \
  --resource-group fcg-rg \
  --name <nome-da-function-app> \
  --function-name ProcessarPaymentProcessed
```

As URLs resultantes seguem o formato:
```
https://<nome-da-function-app>.azurewebsites.net/api/user-created?code=<chave>
```

Elas devem ser configuradas no `NotificationsAPI`, em `appsettings.json`:

```json
"NotificationsFunction": {
  "UserCreatedUrl": "https://<nome>.azurewebsites.net/api/user-created?code=<chave>",
  "PaymentProcessedUrl": "https://<nome>.azurewebsites.net/api/payment-processed?code=<chave>"
}
```

---

## Monitoramento

As execuções ficam visíveis no portal do Azure: **Function App → Functions → selecione a função → Monitor**.

O `az webapp log tail` não captura os logs de execução de Functions — ele expõe o log do App Service, que é uma superfície diferente.

---

## Observações

**Cold start.** A primeira invocação após um período de inatividade leva alguns segundos (medido: ~4,6s), enquanto as seguintes ficam na casa das dezenas de milissegundos. É característica esperada do plano Consumption e aceitável aqui, já que notificações são assíncronas — nenhum usuário aguarda essa resposta em tela.

**Entrega das mensagens.** O adaptador só confirma a mensagem na fila (`ack`) após resposta HTTP bem-sucedida. Uma falha na Function devolve a mensagem ao RabbitMQ em vez de descartá-la silenciosamente.

**DTOs espelhados.** Este repositório é independente e não referencia o projeto `FCG.Contratos`. Os DTOs replicam a forma dos eventos originais — a contrapartida é que mudanças no contrato precisam ser refletidas manualmente aqui.

**Runtime.** `dotnet-isolated` (worker isolado), o modelo atual do Azure Functions. O modelo in-process está em descontinuação.
