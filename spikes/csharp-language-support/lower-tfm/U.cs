namespace Demo;
public record Cat(string Name);
public record Dog(string Name);
public union Pet(Cat, Dog);
public static class Use
{
    public static string Say(Pet p) => p switch { Cat c => c.Name, Dog d => d.Name };
}
