// Lista os quadros de um HEIC dinâmico e os metadados claro/escuro; salva cada quadro em JPG.
// uso: swift frames.swift <arquivo.heic> <pasta_saida>
import Foundation
import ImageIO
import UniformTypeIdentifiers

let src = CGImageSourceCreateWithURL(URL(fileURLWithPath: CommandLine.arguments[1]) as CFURL, nil)!
let n = CGImageSourceGetCount(src)
print("quadros:", n)
if let meta = CGImageSourceCopyMetadataAtIndex(src, 0, nil),
   let tags = CGImageMetadataCopyTags(meta) as? [CGImageMetadataTag] {
    for t in tags { print(CGImageMetadataTagCopyName(t) ?? "", CGImageMetadataTagCopyValue(t) ?? "") }
}
for i in 0..<n {
    let img = CGImageSourceCreateImageAtIndex(src, i, nil)!
    let url = URL(fileURLWithPath: "\(CommandLine.arguments[2])/q\(i).jpg") as CFURL
    let dst = CGImageDestinationCreateWithURL(url, UTType.jpeg.identifier as CFString, 1, nil)!
    CGImageDestinationAddImage(dst, img, [kCGImageDestinationLossyCompressionQuality: 0.88] as CFDictionary)
    CGImageDestinationFinalize(dst)
    print("q\(i)", img.width, img.height)
}
