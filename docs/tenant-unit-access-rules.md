# Empresas, unidades e acesso do MVP

## Status e finalidade

Este documento consolida as decisões aprovadas para a capacidade de empresas e unidades do Brothers Care. Ele é a referência obrigatória para implementação, testes, contratos HTTP e telas relacionadas a tenancy, identidade e autorização.

As regras aqui definidas prevalecem sobre comportamentos mockados do frontend. Pontos identificados como pendentes não devem ser presumidos ou implementados até nova decisão de produto.

## Conceitos

- **Empresa** é o tenant do SaaS e delimita todos os seus dados operacionais e clínicos.
- **Unidade** pertence a uma única empresa.
- **Pessoa** é uma identidade global, identificada pelo CPF. O e-mail é único por pessoa e é usado somente para convite, recuperação e comunicações.
- **Vínculo (membership)** concede à pessoa acesso a uma empresa. Uma mesma pessoa pode ter vínculos ativos em mais de uma empresa.
- **Paciente** é um cadastro interno de uma empresa; não possui login nem acesso ao produto no MVP.

## Identidade, convite e login

1. O CNPJ é obrigatório para cadastrar uma empresa e identifica o tenant de forma única. O mesmo CNPJ não pode gerar dois tenants.
2. Após o pagamento, a empresa informa CNPJ, nome e e-mail do responsável. A plataforma envia a esse e-mail um convite de onboarding vinculado à empresa.
3. No aceite do onboarding, a pessoa cria seu perfil pessoal, informa CPF e define a senha. O CPF identifica a pessoa globalmente e deve ser validado antes de ser persistido.
4. O e-mail é único por pessoa e é usado somente para convite, recuperação e comunicações; não é identificador de login.
5. O login é realizado por CPF e senha.
6. Um mesmo CPF pode possuir vínculo ativo com mais de uma empresa/CNPJ. Depois de autenticar, a pessoa deve obrigatoriamente escolher uma empresa entre os seus vínculos ativos, inclusive em cada novo login. Não há abertura automática pela última empresa usada.
7. Um convite destinado a pessoa já cadastrada reutiliza sua identidade global. O aceite não redefine CPF ou senha; somente concede o vínculo aprovado.
8. A existência de vínculos dessa pessoa em outras empresas nunca é exposta ao administrador que a convida.
9. A empresa selecionada é o contexto ativo da sessão no servidor. Nenhuma rota de negócio deve confiar no tenant enviado pelo cliente como fonte de autorização.

## Papéis e capacidades

No MVP existem somente os perfis abaixo:

| Perfil | Capacidades aprovadas |
| --- | --- |
| Superadministrador da plataforma | Administra informações de plataforma, empresas, unidades e acessos necessários para suporte. Não recebe acesso a dados clínicos. O onboarding comercial cria a empresa e o primeiro vínculo administrativo. |
| Administrador da empresa | Gerencia profissionais, pacientes, unidades e os vínculos entre eles na própria empresa. Pode visualizar integralmente as informações clínicas de sua empresa. Não pode criar empresas. Pode também atuar como profissional de atendimento. |
| Profissional | Atua nas unidades às quais está vinculado e acessa somente os pacientes associados a ele, conforme as regras de vínculo de cuidado. |

- Não haverá recepção, secretaria ou outro perfil no MVP.
- A combinação permitida é administrador e profissional na mesma empresa.
- A definição de papéis distintos para uma mesma pessoa em empresas diferentes permanece fora do escopo até decisão explícita.
- Toda autorização deve ser verificada no servidor; filtros ou ocultação de interface não constituem controle de acesso.

## Empresas e unidades

1. O cadastro comercial cria uma empresa a partir de CNPJ único e envia o convite de onboarding ao responsável informado.
2. Ao aceitar o onboarding, o responsável deve criar obrigatoriamente a primeira clínica/unidade e recebe automaticamente a membership `ClinicAdministrator` da empresa.
3. A empresa não se torna utilizável antes de existir seu primeiro administrador ativo e sua primeira unidade.
4. Um administrador pode criar, editar e desativar unidades apenas da sua própria empresa.
5. O nome de unidade deve ser único dentro da empresa, mas pode se repetir em empresas diferentes.
6. Empresas e unidades não são excluídas fisicamente pelo fluxo do produto. A desativação preserva relacionamentos e histórico.

### Suspensão e reativação da empresa

1. Suspender uma empresa bloqueia imediatamente todos os seus usuários, inclusive sessões já ativas, e interrompe novas operações.
2. O superadministrador continua podendo consultar a empresa suspensa para suporte e reativação, sem obter acesso clínico.
3. A suspensão bloqueia operacionalmente todas as unidades da empresa, mas preserva o estado anterior de cada unidade.
4. Agendamentos futuros da empresa suspensa não são cancelados; ficam bloqueados enquanto durar a suspensão.
5. Ao reativar a empresa, usuários e unidades que estavam ativos antes da suspensão voltam a ficar disponíveis automaticamente. Os agendamentos futuros preservados voltam a ficar disponíveis.
6. Estados que não eram ativos antes da suspensão, como convite pendente, revogado ou desativação própria, não são restaurados como ativos pela reativação da empresa.

### Desativação de unidade

1. A unidade desativada deixa de aceitar novos vínculos, atendimentos ou agendamentos e mantém todo o histórico.
2. Antes de concluir a desativação, os agendamentos futuros dessa unidade devem ser tratados.
3. Todos os agendamentos futuros da unidade são cancelados automaticamente, mesmo que existam outras unidades ativas na empresa.
4. A desativação nunca remaneja automaticamente um agendamento para outra unidade.

## Profissionais, pacientes e vínculo de cuidado

1. Um profissional pode atuar em uma ou mais unidades ativas da mesma empresa.
2. O paciente pertence à empresa, não a uma unidade específica, e pode ser atendido em qualquer unidade ativa dela.
3. Apenas o administrador pode cadastrar um paciente e estabelecer seu vínculo inicial com um profissional.
4. Cada paciente tem exatamente um profissional responsável ativo por empresa.
5. Ao associar o paciente a outro profissional, o vínculo anterior é encerrado automaticamente, o novo vínculo é criado e a mudança deve ser auditada.
6. O profissional responsável pode consultar e operar somente os pacientes que lhe estão associados.
7. Após transferência, o profissional anterior perde acesso de escrita e mantém leitura somente dos dados produzidos até o momento da transferência. O novo profissional recebe o histórico necessário à continuidade do cuidado.
8. O administrador da empresa pode ver integralmente os dados clínicos de sua própria empresa.
9. O superadministrador da plataforma não pode visualizar pacientes, prontuários, evoluções ou qualquer conteúdo clínico.

## Estados e auditoria mínimos

| Recurso | Estados ou comportamento aprovado |
| --- | --- |
| Empresa | Ativa, Suspensa e desativada. Não há exclusão física. |
| Unidade | Ativa e Desativada. Não há exclusão física. |
| Vínculo de empresa | Convite pendente, Ativo, Suspenso ou Revogado. Só o estado ativo permite seleção de empresa e operações. |
| Vínculo de cuidado | Ativo ou Encerrado. A transferência encerra o anterior e cria o novo. |

Devem gerar auditoria no servidor, no mínimo: criação, alteração, suspensão, reativação e desativação de empresa ou unidade; convite e aceite; mudanças de vínculo de empresa; criação de paciente; e transferência de paciente entre profissionais. A auditoria registra ator, empresa, data/hora, ação e recurso, sem incluir conteúdo clínico, senha ou token.

## Decisões ainda pendentes

- Quem pode editar os dados cadastrais da empresa, incluindo nome e CNPJ após o onboarding.
- Regras de formato, validação e tratamento de exceções para CPF e CNPJ, além da validação de unicidade já aprovada.
- Regras completas de criação, inativação e convite de profissionais.
- Detalhamento de quais dados históricos o profissional anterior pode ler após a transferência, respeitando a necessidade clínica e a LGPD.

## Requisitos de implementação

- Cada leitura e escrita de dados de empresa deve validar tenant e vínculo ativo no servidor.
- Relações entre dados pertencentes à empresa devem incluir o escopo de tenant no banco, evitando referências cruzadas.
- CPF, CNPJ, e-mail, tokens, senhas e dados clínicos não podem ser registrados em logs sem política de segurança aprovada.
- Endpoints devem usar Problem Details, OpenAPI, autorização server-side e os testes negativos de acesso cruzado previstos na arquitetura.
- A implementação deve respeitar a direção `Domain <- Application <- Infrastructure`, com endpoints HTTP finos na API.
