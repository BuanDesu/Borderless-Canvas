using BorderlessFlatWindow.Application.Displays;

namespace BorderlessFlatWindow.Application.Ports;

public interface IDesktopDisplayCatalog
{
    DisplayLayout ReadDisplays();
}
