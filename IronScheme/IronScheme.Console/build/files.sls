#| License
Copyright (c) 2007-2016 Llewellyn Pritchard
All rights reserved.
This source code is subject to terms and conditions of the BSD License.
See docs/license.txt. |#

(library (ironscheme files)
  (export
    file-exists?
    delete-file
    get-directory-name
    file-newer?
    file-mtime)
    
  (import 
    (except (rnrs) file-exists? delete-file)
    (only (ironscheme) typed-lambda)
    (ironscheme contracts)
    (ironscheme clr))
    
  (clr-using System.IO)
  (clr-using Oyster.Math)
  (clr-using IronScheme.Scripting.Math)

  (define ->string
    (typed-lambda (str)
      ((Object) String)    
      (if (clr-is String str)
          str
          (clr-call Object ToString str))))   
    
  (define/contract (file-exists? fn:string)
    (clr-static-call File Exists (->string fn)))
    
  (define/contract (delete-file fn:string)
    (clr-static-call File Delete (->string fn)))
    
  (define/contract (get-directory-name path)
    (clr-static-call Path GetDirectoryName (->string path)))   
    
  (define (get-last-write-time filename)
    (clr-static-call File GetLastWriteTime (->string filename)))
    
  (define (file-mtime filename)
    (let ((dt (clr-static-call File (GetLastWriteTime String) filename)))
      (clr-static-call IntX (Create Int64) (clr-prop-get DateTime Ticks dt))))
    
  (define (compare-time t1 t2)
    (clr-call IComparable CompareTo t1 t2))    
    
  (define/contract (file-newer? file1:string file2:string)
    (let ((r (compare-time (get-last-write-time file1)
                           (get-last-write-time file2))))
      (fx>=? r 0))))