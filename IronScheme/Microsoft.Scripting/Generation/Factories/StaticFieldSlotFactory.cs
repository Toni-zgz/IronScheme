/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Microsoft Public License. A 
 * copy of the license can be found in the License.html file at the root of this distribution. If 
 * you cannot locate the  Microsoft Public License, please send an email to 
 * dlr@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Microsoft Public License.
 *
 * You must not remove this notice, or any other, from this software.
 *
 *
 * ***************************************************************************/

using System;
using System.Reflection;
using Microsoft.Scripting.Generation.Slots;

namespace Microsoft.Scripting.Generation.Factories
{

    internal class StaticFieldSlotFactory : SlotFactory
    {
        private TypeGen _typeGen;

        public TypeGen TypeGen { get { return _typeGen; } }

        public StaticFieldSlotFactory(TypeGen typeGen)
        {
            _typeGen = typeGen;
        }

        protected override Slot CreateSlot(SymbolId name, Type type)
        {

            var fb = _typeGen.TypeBuilder.DefineField(SymbolTable.IdToString(name),
              type, FieldAttributes.Assembly | FieldAttributes.Static);

            return new StaticFieldSlot(fb);
        }
    }
}
