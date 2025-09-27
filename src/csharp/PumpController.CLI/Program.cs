using System.Text.Json;
using PumpControllerLib;

var json = await Console.In.ReadToEndAsync();
var input = JsonSerializer.Deserialize<InputDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
    ?? throw new Exception("Invalid JSON input");
var controller = new PumpController();
var result = controller.Evaluate(input.temperatureC, input.pressureBar, input.command);
await Console.Out.WriteAsync(JsonSerializer.Serialize(result));

public class InputDto
{
    public double temperatureC { get; set; }
    public double pressureBar { get; set; }
    public OperatorCommand? command { get; set; }
}
