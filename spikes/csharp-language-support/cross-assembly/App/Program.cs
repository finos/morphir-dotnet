using Acme.Model;
Pet pet = new Dog("Rex");
Order order = new Limit(2m, 10m);
Local local = new B(7);
// No discard arm: each switch must be exhaustive across the assembly boundary.
// Remove one arm, for example `Cat c`, and the compiler reports CS8509.
Console.WriteLine(pet switch { Cat c => "cat " + c.Name, Dog d => "dog " + d.Name });
Console.WriteLine(order switch { Market m => m.Quantity, Limit l => l.Quantity * l.Price });
Console.WriteLine(local switch { A a => a.X, B b => b.Y });
Console.WriteLine(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);

public record A(int X);
public record B(int Y);
public union Local(A, B);   // the application has its own union and its own internal polyfill types
