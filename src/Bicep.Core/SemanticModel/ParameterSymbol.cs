// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Collections.Generic;
using System.Linq;
using Bicep.Core.Diagnostics;
using Bicep.Core.Extensions;
using Bicep.Core.Syntax;
using Bicep.Core.Syntax.Visitors;
using Bicep.Core.TypeSystem;

namespace Bicep.Core.SemanticModel
{
    public class ParameterSymbol : DeclaredSymbol
    {
        public ParameterSymbol(ISymbolContext context, string name, ParameterDeclarationSyntax declaringSyntax, SyntaxBase? modifier)
            : base(context, name, declaringSyntax, declaringSyntax.Name)
        {
            this.Modifier = modifier;
        }

        public ParameterDeclarationSyntax DeclaringParameter => (ParameterDeclarationSyntax) this.DeclaringSyntax;

        public TypeSymbol? TryGetPrimitiveType()
            => LanguageConstants.TryGetDeclarationType(this.DeclaringParameter.Type.TypeName);

        public SyntaxBase? Modifier { get; }

        public override SymbolKind Kind => SymbolKind.Parameter;

        public override void Accept(SymbolVisitor visitor)
        {
            visitor.VisitParameterSymbol(this);
        }

        public override IEnumerable<Symbol> Descendants
        {
            get
            {
                yield return this.Type;
            }
        }

        public override IEnumerable<ErrorDiagnostic> GetDiagnostics()
        {
            var diagnostics = this.ValidateIdentifierAccess();

            switch (this.Modifier)
            {
                case ParameterDefaultValueSyntax defaultValueSyntax:
                    diagnostics = diagnostics.Concat(ValidateDefaultValue(defaultValueSyntax));
                    break;

                case ObjectSyntax modifierSyntax:
                    if (this.Type.TypeKind != TypeKind.Error && this.TryGetPrimitiveType() is TypeSymbol primitiveType)
                    {
                        var modifierType = LanguageConstants.CreateParameterModifierType(primitiveType, this.Type);
                        diagnostics = diagnostics.Concat(TypeValidator.GetExpressionAssignmentDiagnostics(this.Context.TypeManager, modifierSyntax, modifierType));
                    }
                    break;
            }

            return diagnostics;
        }

        private IEnumerable<ErrorDiagnostic> ValidateDefaultValue(ParameterDefaultValueSyntax defaultValueSyntax)
        {
            // figure out type of the default value
            TypeSymbol? defaultValueType = this.Context.TypeManager.GetTypeInfo(defaultValueSyntax.DefaultValue);

            // this type is not a property in a symbol so the semantic error visitor won't collect the errors automatically
            if (defaultValueType is ErrorTypeSymbol)
            {
                return defaultValueType.GetDiagnostics();
            }

            if (TypeValidator.AreTypesAssignable(defaultValueType, this.Type) == false)
            {
                return this.CreateError(defaultValueSyntax.DefaultValue, b => b.ParameterTypeMismatch(this.Type.Name, defaultValueType.Name)).AsEnumerable();
            }

            return Enumerable.Empty<ErrorDiagnostic>();
        }

        private IEnumerable<ErrorDiagnostic> ValidateIdentifierAccess()
        {
            return SyntaxAggregator.Aggregate(this.DeclaringParameter, new List<ErrorDiagnostic>(), (accumulated, current) =>
                {
                    if (current is VariableAccessSyntax)
                    {
                        Symbol? symbol = this.Context.Bindings.TryGetValue(current);
                        
                        // excluded symbol kinds already generate errors - no need to duplicate
                        if (symbol != null &&
                            symbol.Kind != SymbolKind.Error &&
                            symbol.Kind != SymbolKind.Parameter &&
                            symbol.Kind != SymbolKind.Function &&
                            symbol.Kind != SymbolKind.Output)
                        {
                            accumulated.Add(DiagnosticBuilder.ForPosition(current).CannotReferenceSymbolInParamDefaultValue());
                        }
                    }

                    return accumulated;
                },
                accumulated => accumulated);
        }
    }
}
