using System.Reflection;
using System.Runtime.CompilerServices;

class AssemblyVersionIdAttribute : System.Attribute
{

    public int Id;

    public AssemblyVersionIdAttribute(int id) {
        this.Id = id;
    }

}
