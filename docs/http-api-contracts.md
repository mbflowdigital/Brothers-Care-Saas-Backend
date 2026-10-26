# Contratos HTTP e OpenAPI do MVP

## Status e objetivo

Este documento define a proposta de convenções HTTP do Brothers Care. Ele é a referência para a implementação da API e a substituição dos mocks do frontend, mas não afirma que as rotas abaixo já existem.

Todo endpoint implementado deve constar no documento OpenAPI gerado pela aplicação. O OpenAPI publicado é a fonte executável do contrato; este documento explica as decisões transversais que devem permanecer estáveis entre versões.

As regras de negócio e estados estão em [mvp-business-rules.md](mvp-business-rules.md). Quando houver conflito, a decisão de negócio aprovada prevalece e o contrato, os testes e o OpenAPI devem ser atualizados juntos.

## Convenções gerais

| Item | Regra proposta |
| --- | --- |
| Base de rota | `/api/v1` |
| Formato de sucesso | `application/json; charset=utf-8` |
| Formato de erro | `application/problem+json` conforme Problem Details |
| Identificadores | UUID em texto, por exemplo `3fa85f64-5717-4562-b3fc-2c963f66afa6` |
| Nomes de propriedades | `camelCase` no JSON |
| Datas sem horário | ISO 8601 no formato `yyyy-MM-dd` |
| Data e hora | ISO 8601 com offset obrigatório, por exemplo `2026-10-26T14:30:00-03:00` |
| Valores monetários | Inteiro em unidades mínimas da moeda; `amount: 12590` representa R$ 125,90 quando `currency` é `BRL` |
| Campos desconhecidos | Rejeitados em comandos de escrita, salvo decisão explícita por endpoint |
| Exclusão | Não usar `DELETE` para pacientes, profissionais, atendimentos, evoluções ou financeiro no MVP; usar transições de estado autorizadas |

Os payloads não devem enviar `tenantId` para criar, consultar ou alterar recursos da clínica. O tenant é obtido no servidor a partir da sessão autenticada e da membership válida. IDs de outra empresa nunca podem conceder acesso.

## Autenticação e autorização

As rotas privadas exigem autenticação. O mecanismo de sessão ainda será definido na história de autenticação; por isso, este documento não fixa cookie, bearer token ou provedor de identidade.

Cada operação deve declarar no OpenAPI:

- se exige autenticação;
- a capacidade necessária, por exemplo `patient.read` ou `clinical_record.create`;
- o escopo mínimo de tenant, unidade e vínculo de cuidado;
- as respostas `401`, `403` e `404` possíveis.

O servidor retorna:

- `401 Unauthorized` quando a sessão está ausente, inválida ou expirada;
- `403 Forbidden` quando a identidade é válida, o recurso pertence ao tenant atual e a capacidade exigida não foi concedida;
- `404 Not Found` quando o recurso não pertence ao tenant atual ou não existe. Isso evita confirmar a existência de dados de outra empresa.

Superadministradores Brothers não recebem acesso clínico por possuírem acesso administrativo à plataforma. Métricas globais precisam ser agregadas e não conter identificadores de pacientes ou conteúdo clínico.

## Recursos previstos para o MVP

| Área | Rotas propostas | Observação |
| --- | --- | --- |
| Sessão | `POST /sessions`, `GET /session`, `DELETE /session` | Depende da decisão de identidade. |
| Convites e recuperação | `POST /invitations`, `POST /password-recovery-requests`, `POST /password-recovery-confirmations` | Não revelar se e-mail existe. |
| Empresas e unidades | `GET/POST/PATCH /tenants`, `GET/POST/PATCH /clinic-units` | Administração Brothers e administrador da clínica conforme capacidade. |
| Memberships | `GET/POST/PATCH /memberships`, `GET/POST /clinic-unit-memberships` | Papéis e escopos nunca são confiados ao frontend sem autorização. |
| Pacientes | `GET/POST /patients`, `GET/PATCH /patients/{patientId}`, `POST /patients/{patientId}/inactivation` | O payload não contém tenant. |
| Profissionais | `GET/POST /professionals`, `GET/PATCH /professionals/{professionalId}`, `POST /professionals/{professionalId}/inactivation` | Vínculo com usuário terá contrato próprio. |
| Vínculos de cuidado | `GET/POST /care-assignments`, `POST /care-assignments/{assignmentId}/end` | Controla acesso clínico do profissional. |
| Atendimentos | `GET/POST /appointments`, `GET/PATCH /appointments/{appointmentId}`, `POST /appointments/{appointmentId}/complete`, `POST /appointments/{appointmentId}/no-show`, `POST /appointments/{appointmentId}/cancel` | Reagendamento ocorre por `PATCH` em atendimento agendado. |
| Evoluções | `GET/POST /clinical-records`, `GET /clinical-records/{clinicalRecordId}`, `PATCH /clinical-records/{clinicalRecordId}`, `POST /clinical-records/{clinicalRecordId}/finalize` | Retificações recebem rota própria após o modelo ser aprovado. |
| Dashboard | `GET /dashboard/summary` | Retorna apenas indicadores autorizados. |
| Auditoria | `GET /audit-events` | Sem conteúdo clínico no evento. |

Financeiro clínico, convênios, arquivos, comunicação com pacientes, tickets, cobrança automatizada e portal do paciente não entram nos contratos executáveis do primeiro piloto até que o escopo seja aprovado.

## Operações de coleção

### Paginação

Toda coleção potencialmente crescente usa paginação por offset no MVP.

Parâmetros comuns:

| Parâmetro | Tipo | Regra |
| --- | --- | --- |
| `page` | inteiro | Opcional; padrão `1`; mínimo `1`. |
| `pageSize` | inteiro | Opcional; padrão `25`; mínimo `1`; máximo `100`. |
| `sort` | texto | Opcional; campo explicitamente permitido pelo endpoint. |
| `direction` | `asc` ou `desc` | Opcional; padrão definido pela rota. |

Resposta padrão:

```json
{
  "items": [],
  "page": 1,
  "pageSize": 25,
  "totalItems": 0,
  "totalPages": 0
}
```

`totalItems` e `totalPages` são calculados no mesmo escopo de tenant e filtros da coleção. O limite máximo evita consultas excessivas. Caso a operação exija paginação por cursor no futuro, ela será uma nova versão de contrato e não uma troca silenciosa de significado de `page`.

### Busca, filtros e ordenação

Filtros aceitos precisam ser definidos por endpoint e descritos no OpenAPI. Parâmetros desconhecidos, formatos inválidos, filtro repetido quando não suportado ou ordenação por campo não permitido retornam `400` com Problem Details.

| Recurso | Filtros propostos | Ordenação permitida |
| --- | --- | --- |
| Pacientes | `query`, `status`, `clinicUnitId`, `professionalId` | `fullName`, `createdAt` |
| Profissionais | `query`, `status`, `specialty`, `clinicUnitId` | `fullName`, `specialty`, `createdAt` |
| Vínculos de cuidado | `patientId`, `professionalId`, `clinicUnitId`, `status` | `startsOn`, `endsOn` |
| Atendimentos | `from`, `to`, `patientId`, `professionalId`, `clinicUnitId`, `status` | `startsAt`, `endsAt` |
| Evoluções | `patientId`, `appointmentId`, `authorMembershipId`, `status`, `from`, `to` | `createdAt`, `finalizedAt` |
| Auditoria | `from`, `to`, `actorMembershipId`, `action`, `resourceType` | `occurredAt` |

`query` realiza busca textual limitada aos campos informados na rota. Ele não pode pesquisar livremente conteúdo de evoluções ou anexos. Filtros de IDs devem ser validados como UUIDs e também autorizados no servidor.

## Comandos de escrita

### Criação

- `POST` de recurso novo retorna `201 Created`.
- A resposta inclui a representação persistida e cabeçalho `Location` com a rota do recurso.
- O servidor gera ID, timestamps de auditoria e tenant. Esses valores enviados pelo cliente são rejeitados ou ignorados conforme o contrato específico; a preferência é rejeitar para tornar erros evidentes.
- Repetição de criação por falha de rede deve receber chave de idempotência quando o caso de uso puder gerar cobrança, convite, comunicação ou efeito externo. O MVP de pacientes e agenda precisa definir se suportará `Idempotency-Key` antes de ser exposto a clientes instáveis.

### Atualização

- `PATCH` aceita somente campos mutáveis listados no OpenAPI.
- Alterações de estado sensíveis usam comandos explícitos, como `POST /appointments/{id}/cancel`, em vez de `PATCH` genérico.
- Recursos com risco de edição concorrente devem expor `ETag` e exigir `If-Match` em atualizações. Se a versão não corresponder, a API retorna `409 Conflict` com instrução para recarregar o recurso.
- `PUT` não é usado no MVP porque os recursos não devem ser substituídos integralmente por payload de cliente.

### Ações e transições

Toda ação de estado deve ter verbo explícito, corpo mínimo e resposta que represente o estado resultante. Exemplos de nomes: `finalize`, `cancel`, `complete`, `no-show`, `inactivation` e `end`.

O OpenAPI deve descrever os estados de origem permitidos e os códigos possíveis. Uma transição que não é permitida retorna `409 Conflict`, não `400`.

## Datas e horários

- Datas de nascimento, início de vínculo e competência financeira usam `date` sem horário.
- Atendimentos usam `startsAt` e `endsAt` como `date-time` com offset obrigatório.
- Filtros de intervalo usam `from` e `to`, inclusivos, no formato `date-time` com offset. `from` não pode ser posterior a `to`.
- O servidor normaliza comparações para UTC, mas preserva o offset recebido ou apresenta o horário conforme fuso da unidade na resposta, decisão que deve ser implementada de forma consistente.
- Datas relativas como “hoje” e “amanhã” não são valores de contrato; são responsabilidade de apresentação do frontend.

## Valores monetários

Financeiro e assinaturas devem usar objeto monetário explícito, sem `float` ou `double`:

```json
{
  "amount": 12590,
  "currency": "BRL"
}
```

- `amount` é inteiro e representa a menor unidade da moeda.
- `currency` segue ISO 4217 e, no MVP brasileiro, aceita apenas `BRL`.
- Valores negativos só são aceitos em campos cuja semântica declare ajuste ou estorno.
- A interface formata valores para a pessoa usuária; a API não recebe ou retorna textos como `R$ 125,90`.
- Totais retornados precisam informar claramente a competência, filtros e moeda usados no cálculo.

## Validação de entrada

Validações de contrato são executadas antes do caso de uso. Regras de autorização, isolamento de tenant, conflitos de agenda e transições de estado também são verificadas no servidor, mas podem resultar em outro tipo de erro.

| Situação | Resposta | Tipo de problema proposto |
| --- | --- | --- |
| JSON inválido ou tipo incompatível | `400` | `https://api.brotherscare.com/problems/validation` |
| Campo obrigatório ausente ou formato inválido | `400` | `https://api.brotherscare.com/problems/validation` |
| Filtro, paginação ou ordenação inválidos | `400` | `https://api.brotherscare.com/problems/validation` |
| Sessão ausente ou expirada | `401` | `https://api.brotherscare.com/problems/authentication-required` |
| Capacidade insuficiente no próprio tenant | `403` | `https://api.brotherscare.com/problems/forbidden` |
| Recurso inexistente ou de outro tenant | `404` | `https://api.brotherscare.com/problems/not-found` |
| Estado incompatível ou edição concorrente | `409` | `https://api.brotherscare.com/problems/conflict` |
| Regra de domínio violada, como conflito de agenda | `422` | `https://api.brotherscare.com/problems/business-rule` |
| Limite de plano excedido | `422` | `https://api.brotherscare.com/problems/plan-limit` |
| Erro não tratado | `500` | `https://api.brotherscare.com/problems/unexpected` |

## Problem Details

Todos os erros usam a estrutura base do Problem Details. O servidor nunca retorna detalhes de exceções internas, credenciais, tokens, texto clínico ou identificadores de recursos de outro tenant.

Exemplo de erro de validação:

```json
{
  "type": "https://api.brotherscare.com/problems/validation",
  "title": "A solicitação contém campos inválidos.",
  "status": 400,
  "detail": "Revise os campos informados e tente novamente.",
  "instance": "/api/v1/appointments",
  "traceId": "00-...",
  "errors": {
    "startsAt": [
      "Informe data e hora com offset."
    ],
    "endsAt": [
      "O término deve ser posterior ao início."
    ]
  }
}
```

Extensões padronizadas:

| Campo | Uso |
| --- | --- |
| `traceId` | Correlação com logs internos, sem expor informação sensível. |
| `errors` | Mapa de erros por campo para validações de entrada. |
| `code` | Código estável de regra de negócio quando o frontend precisa diferenciar uma ação, por exemplo `appointment_overlap`. |
| `retryAfterSeconds` | Apenas quando o cliente puder repetir de forma segura após período informado. |

`detail` é legível para a pessoa usuária, mas o frontend não deve tomar decisões por comparação textual. Para fluxos que dependem de comportamento, deve usar `status`, `type` e `code` estáveis.

## Requisitos de OpenAPI

- O documento é publicado somente em desenvolvimento até que a política de exposição de API seja aprovada.
- Cada operação deve ter `operationId` estável, tag de domínio, resumo em português brasileiro e respostas de sucesso e erro documentadas.
- Schemas de entrada e saída são separados quando seus campos tiverem responsabilidades diferentes.
- Os schemas devem declarar formato de UUID, `date`, `date-time`, enumerações e limites de tamanho conhecidos.
- Endpoints paginados reutilizam o schema de envelope somente quando a forma de resposta é realmente idêntica; não criar abstrações genéricas apenas por conveniência.
- Exemplos não podem conter dados clínicos reais, credenciais, tokens ou e-mails pessoais.
- Mudanças incompatíveis exigem nova versão de rota ou período de compatibilidade documentado; não alterar silenciosamente o significado de campo ou estado já publicado.

## Critérios de aceite por endpoint

Antes de publicar uma rota, validar:

1. Operação documentada no OpenAPI e alinhada a uma regra aprovada.
2. Tenant, unidade, papel e vínculo de cuidado validados no servidor quando aplicável.
3. Entrada, paginação, filtros e ordenação validados e cobertos por testes.
4. Códigos de resposta e Problem Details cobertos por testes positivos e negativos.
5. Nenhum dado de outro tenant é retornado, inclusive em erro, ordenação, totalização ou busca.
6. Datas e valores monetários respeitam as convenções deste documento.
7. Logs, telemetria e respostas não expõem dados clínicos, credenciais ou tokens.
