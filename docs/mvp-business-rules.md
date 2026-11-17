# Regras de negócio do MVP

## Objetivo e status

Este documento é a proposta de regras para o MVP do Brothers Care. Ele separa as decisões de produto que precisam de aprovação do comportamento demonstrativo existente no frontend. Nenhuma tela mockada, dado de apresentação ou mensagem de sucesso deve ser interpretado como regra aprovada.

As regras identificadas como **propostas** orientam a implementação e os testes somente depois de aprovadas pela operação clínica e pela Brothers Digital. O schema atual contém apenas a estrutura mínima para tenants, unidades, usuários, memberships, pacientes, profissionais, vínculos de cuidado, atendimentos e evoluções; ele não implementa autenticação, autorização ou casos de uso.

## Regras transversais

### Tenant e unidade

- Cada empresa cliente é um `tenant` e é dona de todos os dados operacionais e clínicos criados em seu contexto.
- Todo registro operacional ou clínico deve ter `tenant_id` obrigatório. `users`, `tenants` e o catálogo comercial são globais.
- O tenant é derivado da identidade autenticada e de uma membership válida. A API nunca aceita o tenant como fonte confiável apenas porque veio no corpo, rota, query string ou navegador.
- Um registro só pode referenciar outro registro do mesmo tenant. As relações persistentes devem incluir `tenant_id` nas chaves estrangeiras quando ambos os lados pertencem a uma empresa.
- Uma unidade pertence a um único tenant. A atuação de uma pessoa em uma unidade exige membership ativa na empresa e vínculo explícito com a unidade.
- Dados de outro tenant retornam acesso negado ou recurso não encontrado, sem revelar sua existência.

### Administração da plataforma

- `platform_administrators` representa acesso global da Brothers Digital e não possui `tenant_id`.
- Um administrador de plataforma referencia uma identidade global em `users`. A mesma identidade pode também possuir memberships de tenant, mas o papel de plataforma não concede acesso clínico por si só.
- O papel inicial é `SuperAdministrator`; ele administra tenants, planos, assinaturas e usuários internos da plataforma conforme autorização futura.
- O superadministrador não pode consultar pacientes identificáveis, evoluções, anexos ou conteúdo clínico apenas por possuir papel global.
- O primeiro superadministrador é provisionado por comando explícito e confirmação de ambiente. Credencial, sessão e recuperação de acesso serão criadas na tarefa de autenticação.

### Histórico, inativação e auditoria

- Registros usados em atendimento, evolução ou financeiro não são excluídos fisicamente pelo fluxo comum do produto.
- Pacientes, profissionais, vínculos e unidades deixam de ser usados por inativação ou encerramento, preservando o histórico.
- Ações sensíveis precisam gerar evento de auditoria no servidor: mudança de papel, vínculo de unidade, vínculo de cuidado, atendimento, evolução finalizada, retificação, fechamento financeiro e alteração de assinatura.
- O evento de auditoria deve registrar ator, tenant, data/hora, tipo de ação e recurso afetado. Conteúdo clínico, senha, token e anexos não entram no payload de auditoria.

### Datas, horário e concorrência

- Datas e horários de atendimentos são armazenados com offset. A unidade deve possuir fuso horário configurado antes de a agenda ser disponibilizada em produção.
- Regras de conflito de agenda são validadas no servidor dentro do caso de uso. Índices aceleram a verificação, mas não substituem controle de concorrência.
- Estados só mudam por transições permitidas. O cliente pode sugerir uma ação; a API decide se ela é válida.

## Pacientes

### Informações mínimas propostas

- Nome completo, data de nascimento e estado do cadastro são obrigatórios.
- Dados de contato, responsável, convênio, consentimentos e documentos serão definidos em histórias próprias. Eles não devem ser acrescentados por inferência a partir do mock.
- O paciente pertence a exatamente um tenant. Seu atendimento pode ocorrer em mais de uma unidade do mesmo tenant conforme vínculos de cuidado ativos.

### Estados e transições propostas

| Estado | Significado | Transições permitidas |
| --- | --- | --- |
| `Ativo` | Pode receber novos vínculos e atendimentos. | `Inativo` |
| `Inativo` | Não recebe novos atendimentos ou vínculos; mantém histórico. | `Ativo` |

### Regras propostas

- Inativar um paciente exige ausência de atendimento futuro ativo ou o cancelamento/reagendamento explícito desses atendimentos.
- Reativar não restaura automaticamente vínculos de cuidado encerrados.
- O acesso clínico do profissional depende de vínculo de cuidado ativo, não apenas da existência do cadastro do paciente.
- A operação deve definir os campos usados para identificar possível duplicidade. Até essa decisão, o sistema não deve bloquear cadastros apenas por nome ou data de nascimento.

## Profissionais

### Informações mínimas propostas

- Nome, especialidade, número de registro profissional, tenant e estado são obrigatórios.
- O registro profissional é único por tenant na proposta inicial. A validação nacional por conselho profissional é uma integração futura, não uma regra já atendida.
- Um profissional pode atuar em uma ou mais unidades do mesmo tenant quando sua membership de unidade estiver ativa.
- A vinculação entre `User` e `Professional` precisa ser definida na história de convite/ativação. Um cadastro profissional não concede acesso à plataforma por si só.

### Estados e transições propostas

| Estado | Significado | Transições permitidas |
| --- | --- | --- |
| `Ativo` | Pode receber vínculos de cuidado e novos atendimentos. | `Inativo` |
| `Inativo` | Não recebe novos vínculos ou atendimentos; mantém histórico. | `Ativo` |

### Regras propostas

- Inativar um profissional exige tratar atendimentos futuros: transferir, cancelar ou reagendar de forma explícita.
- A inativação não remove autoria de evoluções nem registros passados de agenda.
- Afastamento, férias e bloqueio temporário não entram no MVP como estado do profissional. Se forem necessários, devem ser modelados como disponibilidade ou ausência, sem reutilizar `Inativo`.

## Vínculo de cuidado

- Um vínculo de cuidado associa paciente, profissional, unidade e tenant, com início, encerramento e estado.
- O vínculo ativo é a condição mínima para o profissional consultar dados clínicos identificáveis do paciente e criar evolução para ele.
- O administrador clínico pode criar e encerrar vínculos conforme sua capacidade autorizada. A recepção não visualiza conteúdo clínico.
- Compartilhamento de paciente entre profissionais exige vínculos independentes ou uma regra explícita de equipe; não ocorre implicitamente por pertencerem ao mesmo tenant.

| Estado | Significado | Transições permitidas |
| --- | --- | --- |
| `Ativo` | Autoriza o escopo clínico previsto para o profissional e unidade. | `Encerrado` |
| `Encerrado` | Preserva histórico; não autoriza novos atendimentos nem acesso clínico futuro. | Nenhuma; criar novo vínculo para retomar o cuidado. |

## Agenda e atendimentos

As regras de criação e transição desta seção estão aprovadas para o fluxo em que o profissional agenda um atendimento para um paciente sob seus cuidados. A leitura da agenda também permite que um administrador da clínica visualize os agendamentos do tenant ativo, com filtros por profissional e unidade; essa permissão não autoriza acesso a outro tenant.

### Informações mínimas

- Um atendimento referencia obrigatoriamente tenant, unidade, paciente, profissional, início, fim, estado, ator da criação e data da criação.
- A observação administrativa do agendamento é opcional, possui no máximo 1.000 caracteres e não deve ser usada para armazenar evolução ou informação clínica.
- O tenant, o profissional e o ator são derivados da sessão autenticada. O comando enviado pelo profissional não aceita `tenantId`, `professionalId` ou identificador do ator como fonte confiável.
- Datas e horas usam `date-time` com offset obrigatório. O horário final deve ser posterior ao inicial.

### Autorização e escopo

- O profissional autenticado agenda somente para si mesmo.
- Tenant, unidade, paciente, profissional, membership e vínculo de cuidado devem estar ativos no momento da criação.
- Todos os recursos envolvidos devem pertencer ao tenant ativo da sessão.
- O paciente deve possuir vínculo de cuidado ativo com o profissional. O agendamento não cria esse vínculo automaticamente.
- O profissional deve possuir vínculo ativo com a unidade escolhida.
- O paciente pode ser atendido em qualquer unidade ativa do tenant em que o profissional possua vínculo ativo; a unidade registrada originalmente no vínculo de cuidado não limita o local do atendimento.
- Um profissional não pode consultar ou alterar agendamentos de outro profissional, mesmo que envie outro `professionalId` como filtro. Administradores da clínica podem consultar agendas de profissionais somente no tenant ativo.
- Outros papéis não recebem acesso à agenda por inferência; uma permissão específica deve ser aprovada e validada no servidor.
- Empresa suspensa ou unidade inativa não aceita novos agendamentos. Aplicam-se também as regras de suspensão e desativação descritas em `tenant-unit-access-rules.md`.

### Estados e transições

| Estado | Significado | Transições permitidas |
| --- | --- | --- |
| `Agendado` | Horário reservado e ainda não realizado. | `Realizado`, `Falta`, `Cancelado` ou reagendamento. |
| `Realizado` | Atendimento ocorreu. | Nenhuma transição direta; correções exigem fluxo auditado a definir. |
| `Falta` | Paciente não compareceu. | Nenhuma transição direta; correções exigem fluxo auditado a definir. |
| `Cancelado` | Atendimento não ocorrerá. | Nenhuma; um novo atendimento pode ser criado. |

### Criação, conflito e concorrência

- Não é permitido criar um agendamento cujo início esteja no passado no instante em que o servidor processa a operação.
- Um atendimento deve possuir duração máxima de 24 horas. Durações padrão por serviço ou profissional serão definidas junto da disponibilidade.
- Não pode haver sobreposição de agendamentos em estado `Agendado` para o mesmo profissional.
- Não pode haver sobreposição de agendamentos em estado `Agendado` para o mesmo paciente, mesmo que sejam com profissionais ou unidades diferentes.
- Horários adjacentes não são sobreposição. Por exemplo, `09:00–10:00` e `10:00–11:00` são válidos.
- A sobreposição ocorre quando `existente.inicio < novo.fim` e `existente.fim > novo.inicio`.
- Agendamentos `Realizado`, `Falta` e `Cancelado` não reservam horário para a validação de novos agendamentos.
- A verificação de conflito e a gravação devem ocorrer na mesma transação e considerar requisições concorrentes.
- A jornada semanal é configurada por profissional e unidade, usando o fuso horário local da unidade. Uma jornada vazia ainda permite agendamentos sem validação do expediente como regra temporária de transição.
- Bloqueios pontuais e férias são globais para o profissional, independentemente da unidade, e impedem novos agendamentos sobrepostos.
- Só é possível criar um bloqueio ou período de férias quando não houver agendamento `Agendado` sobreposto. Horários adjacentes são permitidos; atendimentos existentes não são cancelados nem alterados.
- O profissional gerencia somente a própria disponibilidade. O administrador da clínica pode gerenciar profissionais do tenant ativo; nenhum desses acessos alcança outro tenant.

### Reagendamento, cancelamento e encerramento

- Reagendar altera início, fim e, quando solicitado, unidade de um atendimento em estado `Agendado`, registra auditoria e revalida todas as regras de escopo e conflito. Não cria outro atendimento.
- Cancelar, marcar falta ou concluir só é permitido ao profissional responsável enquanto o atendimento estiver `Agendado`.
- Cancelamento e falta devem registrar motivo, ator e data/hora. No MVP, o motivo é texto livre obrigatório de até 500 caracteres; um catálogo poderá ser criado posteriormente.
- Um atendimento só pode ser marcado como `Realizado` ou `Falta` quando seu horário de início tiver sido alcançado.
- Cancelamento preserva o atendimento e libera o intervalo para novos agendamentos.
- `Realizado`, `Falta` e `Cancelado` são estados finais no MVP. Correções exigirão um fluxo auditado futuro.
- Não é permitido finalizar uma evolução para atendimento cancelado ou marcado como falta.

### Consulta

- A agenda do profissional retorna somente atendimentos em que ele seja o profissional responsável. O administrador da clínica pode consultar profissionais do tenant ativo e filtrar opcionalmente por profissional e unidade.
- Toda consulta de agenda exige intervalo inicial e final, com período máximo de 93 dias por requisição.
- A resposta pode conter dados cadastrais mínimos do paciente, profissional e unidade necessários à agenda, sem expor conteúdo de evoluções clínicas.
- A interface apresenta os horários no fuso da unidade selecionada. Esse fuso é retornado junto às unidades e aos agendamentos.

## Evoluções clínicas

### Informações mínimas propostas

- Uma evolução está vinculada a um atendimento realizado, paciente, tenant e autor identificado pela membership.
- O conteúdo clínico é dado sensível. A API valida tenant, unidade, papel e vínculo de cuidado antes de leitura ou escrita.
- Cada atendimento possui no máximo uma evolução principal no MVP. Complementos e retificações são registros distintos, nunca sobrescrita silenciosa da evolução finalizada.

### Estados e transições propostas

| Estado | Significado | Transições permitidas |
| --- | --- | --- |
| `Rascunho` | Pode ser alterado pelo autor autorizado. | `Finalizada` |
| `Finalizada` | Registra autoria e data de finalização; não pode ser alterada. | Nenhuma; somente retificação separada. |

### Regras propostas

- Apenas o autor pode editar seu rascunho, salvo capacidade excepcional definida e auditada para coordenação clínica.
- Finalizar exige campos clínicos mínimos a serem definidos pela responsável técnica. O MVP não deve adotar os campos do mock como padrão obrigatório sem essa aprovação.
- A retificação deve referenciar a evolução original, ter autor e data próprios e informar a justificativa. Esse modelo ainda precisa ser acrescentado ao schema antes de implementar o fluxo.
- Anexos clínicos não entram no MVP até que haja política de tipo de arquivo, antivírus, armazenamento privado, retenção e autorização de download.

## Financeiro da clínica

O financeiro clínico não faz parte do núcleo inicial de agenda e evolução. As regras abaixo são propostas para a fase posterior e não devem ser implementadas ou exibidas como operação real antes de aprovação.

### Separação obrigatória

- Financeiro da clínica registra produção, convênios, recebimentos e repasses da empresa cliente.
- Assinatura Brothers Care registra cobrança pelo uso da plataforma.
- São domínios separados, com permissões, saldos e trilhas de auditoria independentes.

### Estados propostos para lançamento financeiro

| Estado | Significado | Transições permitidas |
| --- | --- | --- |
| `Rascunho` | Lançamento ainda pode ser ajustado. | `Em aberto`, `Cancelado` |
| `Em aberto` | Valor devido ou a receber. | `Parcialmente recebido`, `Recebido`, `Cancelado`, `Baixado` |
| `Parcialmente recebido` | Há recebimento inferior ao valor total. | `Recebido`, `Cancelado`, `Baixado` |
| `Recebido` | Valor conciliado integralmente. | Nenhuma; ajustes geram novo lançamento. |
| `Cancelado` | Lançamento anulado antes da liquidação. | Nenhuma. |
| `Baixado` | Saldo encerrado por perda, glosa ou decisão aprovada. | Nenhuma. |

### Fechamento por competência proposto

| Estado | Significado | Transições permitidas |
| --- | --- | --- |
| `Aberto` | Período em preparação. | `Em conferência` |
| `Em conferência` | Produção e divergências em análise. | `Aberto`, `Fechado` |
| `Fechado` | Período bloqueado para alterações ordinárias. | `Reaberto` por capacidade autorizada |
| `Reaberto` | Correção auditada em andamento. | `Em conferência` |

## Assinaturas Brothers Care

### Regras propostas

- Uma assinatura pertence a um tenant e referencia um plano comercial global.
- A assinatura controla acesso comercial à plataforma, não altera lançamentos do financeiro clínico.
- Limites do plano devem ser aplicados no servidor e exibidos como informação no frontend. O frontend não é a fonte de bloqueio.
- Cobrança manual é suficiente para o MVP comercial enquanto não houver integração com gateway e processamento idempotente de webhooks.

### Estados e transições propostos

| Estado | Significado | Transições permitidas |
| --- | --- | --- |
| `Avaliação` | Período inicial autorizado. | `Ativa`, `Cancelada` |
| `Ativa` | Tenant habilitado conforme plano. | `Em atraso`, `Suspensa`, `Cancelada` |
| `Em atraso` | Cobrança pendente; regras de tolerância devem ser definidas. | `Ativa`, `Suspensa`, `Cancelada` |
| `Suspensa` | Acesso operacional bloqueado conforme política comercial. | `Ativa`, `Cancelada` |
| `Cancelada` | Assinatura encerrada. | Nenhuma; nova assinatura para retomar. |

## Decisões pendentes antes da implementação

1. Quais campos clínicos são obrigatórios para finalizar uma evolução e quem aprova esse modelo?
R: Definir ainda
2. Administrador da clínica pode ler conteúdo de evolução por padrão ou precisa da capacidade explícita `clinical_record.read_all`?
R: Pode ver tudo por padrão
3. Profissionais da mesma equipe podem acessar o histórico uns dos outros sem vínculo próprio de cuidado?
R: cada profissional ve apenas o seu
4. Quais conflitos de agenda devem ser bloqueados além do profissional: paciente, sala, equipamento e deslocamento domiciliar?
R: ND
5. Quais motivos de cancelamento, falta, baixa e retificação devem ser obrigatórios?
R:ND
6. O financeiro da clínica faz parte do primeiro piloto ou permanece fora do MVP?
R: Precisa estar no piloto
7. Qual é a política de período de tolerância, suspensão e reativação de assinaturas?
R: ND
8. Quais regras de retenção, exportação, exclusão e acesso excepcional a dados clínicos serão aprovadas antes do uso com dados reais?
R: ND

## Critérios para aprovar uma regra

Uma regra entra em implementação somente quando tiver responsável de negócio, descrição do comportamento, estados permitidos, atores autorizados, mensagens de erro esperadas, eventos de auditoria e casos de teste positivos e negativos. Mudanças posteriores devem atualizar este documento, a migração quando necessário e os testes do domínio e da API.
