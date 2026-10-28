import type { ProductionLocale } from "./productionCatalog";

/** Required keys for the public entry experience. */
export const REQUIRED_ENTRY_I18N_KEYS = [
  "entry.hero.eyebrow",
  "entry.hero.title",
  "entry.hero.lead",
  "entry.hero.notOnlyRecipes",
  "entry.cta.login",
  "entry.cta.secondary",
  "entry.demo.title",
  "entry.demo.step1",
  "entry.demo.step2",
  "entry.demo.step3",
  "entry.demo.step4",
  "entry.demo.step5",
  "entry.why.title",
  "entry.why.account",
  "entry.outcomes.title",
  "entry.outcomes.inventory",
  "entry.outcomes.decide",
  "entry.outcomes.attention",
  "entry.outcomes.plan",
  "entry.outcomes.adapt",
  "entry.adultNotice",
  "entry.legal.terms",
  "entry.legal.privacy",
  "entry.legal.placeholderNote",
] as const;

type Catalog = Record<string, string>;

export const entryCatalogs: Record<ProductionLocale, Catalog> = {
  "pt-BR": {
    "entry.hero.eyebrow": "Para adultos que querem cozinhar com o que têm",
    "entry.hero.title": "Transforme alimentos disponíveis em refeições úteis",
    "entry.hero.lead":
      "O Cocinaris ajuda a decidir o que preparar com inventário, urgência, planos, preferências, tempo e esforço — com menos desperdício e menos dependência de delivery.",
    "entry.hero.notOnlyRecipes":
      "Não é só um gerador de receitas. O centro é usar melhor a comida que você já tem ou planejou.",
    "entry.cta.login": "Criar conta ou entrar",
    "entry.cta.secondary": "Ver como funciona",
    "entry.demo.title": "Do alimento à refeição — e de volta ao inventário",
    "entry.demo.step1": "Alimentos na despensa, geladeira ou freezer",
    "entry.demo.step2": "Quantidade, estado, urgência e contexto ficam claros",
    "entry.demo.step3": "Você recebe possibilidades de preparo explicáveis",
    "entry.demo.step4": "Escolhe, prepara e cozinha no seu ritmo",
    "entry.demo.step5":
      "Ao concluir, consumo, sobras, congelamento e desperdício são reconciliados",
    "entry.why.title": "Por que criar uma conta?",
    "entry.why.account":
      "Com uma conta, o produto pode lembrar inventário, preferências confirmadas e planos aceitos para responder “o que cozinhar hoje?” com contexto real — sem exigir que você conte a cozinha do zero a cada visita.",
    "entry.outcomes.title": "O que o Cocinaris ajuda a resolver",
    "entry.outcomes.inventory": "Entender o que está disponível e utilizável",
    "entry.outcomes.decide": "Decidir o que preparar agora",
    "entry.outcomes.attention":
      "Usar alimentos que pedem atenção — sem forçar uma escolha",
    "entry.outcomes.plan": "Planejar refeições e compras com flexibilidade",
    "entry.outcomes.adapt":
      "Adaptar sugestões a preferências, restrições, equipamento, tempo e limpeza",
    "entry.adultNotice":
      "Serviço destinado a adultos. Declarações e consentimentos aplicáveis são tratados no fluxo de conta.",
    "entry.legal.terms": "Termos (texto jurídico em preparação)",
    "entry.legal.privacy": "Privacidade (texto jurídico em preparação)",
    "entry.legal.placeholderNote":
      "Links jurídicos são placeholders até a revisão legal definitiva.",
  },
  en: {
    "entry.hero.eyebrow": "For adults who want to cook with what they have",
    "entry.hero.title": "Turn available food into useful meals",
    "entry.hero.lead":
      "Cocinaris helps you decide what to prepare using inventory, urgency, plans, preferences, time, and effort — with less waste and less delivery dependence.",
    "entry.hero.notOnlyRecipes":
      "It is not just a recipe generator. The center is making better use of food you already have or planned.",
    "entry.cta.login": "Create an account or sign in",
    "entry.cta.secondary": "See how it works",
    "entry.demo.title": "From food to meal — and back to inventory",
    "entry.demo.step1": "Food in the pantry, refrigerator, or freezer",
    "entry.demo.step2": "Quantity, state, urgency, and context become clear",
    "entry.demo.step3": "You receive explainable preparation possibilities",
    "entry.demo.step4": "You choose, prepare, and cook at your pace",
    "entry.demo.step5":
      "On completion, consumption, leftovers, freezing, and waste are reconciled",
    "entry.why.title": "Why create an account?",
    "entry.why.account":
      "With an account, the product can remember inventory, confirmed preferences, and accepted plans to answer “what shall we cook today?” with real context — without starting from scratch every visit.",
    "entry.outcomes.title": "What Cocinaris helps solve",
    "entry.outcomes.inventory": "Understand what food is available and usable",
    "entry.outcomes.decide": "Decide what to prepare now",
    "entry.outcomes.attention":
      "Use food that needs attention — without forcing a choice",
    "entry.outcomes.plan": "Plan meals and purchases with flexibility",
    "entry.outcomes.adapt":
      "Adapt suggestions to preferences, restrictions, equipment, time, and cleanup",
    "entry.adultNotice":
      "Service intended for adults. Applicable declarations and consents are handled in the account flow.",
    "entry.legal.terms": "Terms (legal text forthcoming)",
    "entry.legal.privacy": "Privacy (legal text forthcoming)",
    "entry.legal.placeholderNote":
      "Legal links are placeholders until definitive legal review.",
  },
  es: {
    "entry.hero.eyebrow": "Para adultos que quieren cocinar con lo que tienen",
    "entry.hero.title": "Convierte alimentos disponibles en comidas útiles",
    "entry.hero.lead":
      "Cocinaris ayuda a decidir qué preparar usando inventario, urgencia, planes, preferencias, tiempo y esfuerzo — con menos desperdicio y menos dependencia del delivery.",
    "entry.hero.notOnlyRecipes":
      "No es solo un generador de recetas. El centro es aprovechar mejor la comida que ya tienes o planificaste.",
    "entry.cta.login": "Crear cuenta o iniciar sesión",
    "entry.cta.secondary": "Ver cómo funciona",
    "entry.demo.title": "De alimento a comida — y de vuelta al inventario",
    "entry.demo.step1": "Alimentos en la despensa, nevera o congelador",
    "entry.demo.step2": "Cantidad, estado, urgencia y contexto quedan claros",
    "entry.demo.step3": "Recibes posibilidades de preparación explicables",
    "entry.demo.step4": "Eliges, preparas y cocinas a tu ritmo",
    "entry.demo.step5":
      "Al terminar, consumo, sobras, congelación y desperdicio se reconcilian",
    "entry.why.title": "¿Por qué crear una cuenta?",
    "entry.why.account":
      "Con una cuenta, el producto puede recordar inventario, preferencias confirmadas y planes aceptados para responder “¿qué cocinamos hoy?” con contexto real — sin empezar de cero en cada visita.",
    "entry.outcomes.title": "Qué ayuda a resolver Cocinaris",
    "entry.outcomes.inventory": "Entender qué comida está disponible y usable",
    "entry.outcomes.decide": "Decidir qué preparar ahora",
    "entry.outcomes.attention":
      "Usar alimentos que piden atención — sin forzar una elección",
    "entry.outcomes.plan": "Planificar comidas y compras con flexibilidad",
    "entry.outcomes.adapt":
      "Adaptar sugerencias a preferencias, restricciones, equipo, tiempo y limpieza",
    "entry.adultNotice":
      "Servicio destinado a adultos. Las declaraciones y consentimientos aplicables se tratan en el flujo de cuenta.",
    "entry.legal.terms": "Términos (texto jurídico en preparación)",
    "entry.legal.privacy": "Privacidad (texto jurídico en preparación)",
    "entry.legal.placeholderNote":
      "Los enlaces jurídicos son marcadores de posición hasta la revisión legal definitiva.",
  },
};
