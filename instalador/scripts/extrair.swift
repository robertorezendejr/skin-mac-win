// Renderiza o ícone (já no visual atual do macOS) de cada caminho em PNG 1024px.
// uso: swift extrair.swift <pasta_saida> <nome=caminho>...
import AppKit

let out = CommandLine.arguments[1]
for arg in CommandLine.arguments.dropFirst(2) {
    let parts = arg.split(separator: "=", maxSplits: 1).map(String.init)
    let (name, path) = (parts[0], parts[1])
    let img: NSImage
    if path.hasSuffix(".icns") {
        guard let i = NSImage(contentsOfFile: path) else { print("falhou: \(path)"); continue }
        img = i
    } else {
        img = NSWorkspace.shared.icon(forFile: path)
    }
    let size = 1024
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: size, pixelsHigh: size, bitsPerSample: 8,
                               samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
                               colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    img.draw(in: NSRect(x: 0, y: 0, width: size, height: size))
    NSGraphicsContext.restoreGraphicsState()
    try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: "\(out)/\(name).png"))
    print("ok: \(name)")
}
