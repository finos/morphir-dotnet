namespace Morphir.IR;

public interface IType<out TAttributes>
{
    TAttributes Attributes { get; }   
}
